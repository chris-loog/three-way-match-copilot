using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Infrastructure.Explanation;
using ThreeWayMatch.Infrastructure.Persistence;

namespace DocGen.Explanation;

/// <summary>
/// Quality check for the explanations: writes one for every document waiting for a decision (up to --limit), prints how many
/// came from the model and how many fell back to the template, and writes a markdown file to read and score by hand.
/// Calls Azure OpenAI and the database; Chris runs it.
/// </summary>
internal static class ExplainCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("THREEWAYMATCH_SQL");
        var endpoint = Environment.GetEnvironmentVariable("THREEWAYMATCH_OPENAI_ENDPOINT");
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(endpoint))
        {
            Console.Error.WriteLine("Set THREEWAYMATCH_SQL and THREEWAYMATCH_OPENAI_ENDPOINT (for example https://aoai-three-way-match.openai.azure.com/).");
            return 1;
        }

        var limit = int.Parse(ValueOf(args, "--limit") ?? "20", CultureInfo.InvariantCulture);
        var output = ValueOf(args, "--out") ?? Path.Combine("data", "review", "explanations.md");

        var dbOptions = new DbContextOptionsBuilder<ThreeWayMatchDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null))
            .Options;
        await using var db = new ThreeWayMatchDbContext(dbOptions);

        var llm = ExplanationRegistration.CreateClient(new ExplanationOptions { Endpoint = new Uri(endpoint) });
        var service = new ExplanationService(db, llm, TimeProvider.System, NullLogger<ExplanationService>.Instance);

        var ids = await db.MatchResults.AsNoTracking()
            .Where(m => (m.Decision == Decision.NeedsReview || m.Decision == Decision.OnHold) && m.ReviewedAt == null)
            .OrderBy(m => m.DocumentId)
            .Select(m => m.DocumentId)
            .Take(limit)
            .ToListAsync();

        Console.WriteLine($"Explaining {ids.Count} documents.");
        var report = new StringBuilder("# Explanations to score by hand\n\nScore each 1 to 5 for: correct, useful, safe to send. Note anything wrong.\n\n");
        int fromModel = 0, fromTemplate = 0, promptTokens = 0, completionTokens = 0;

        foreach (var id in ids)
        {
            var outcome = await service.GenerateAsync(id, CancellationToken.None);
            var stored = await db.Explanations.AsNoTracking().Include(e => e.Document).SingleOrDefaultAsync(e => e.DocumentId == id);
            if (!outcome.Succeeded || stored is null)
            {
                Console.WriteLine($"document {id}: {outcome.Message}");
                continue;
            }

            if (stored.Source == ExplanationSource.Model)
            {
                fromModel++;
            }
            else
            {
                fromTemplate++;
            }

            promptTokens += stored.PromptTokens;
            completionTokens += stored.CompletionTokens;
            Console.WriteLine($"document {id}: {stored.Source}{(stored.GuardNotes is null ? string.Empty : "  (" + stored.GuardNotes + ")")}");
            report.Append(CultureInfo.InvariantCulture, $"## Document {id} ({stored.Source})\n\n**Summary:** {stored.Summary}\n\n**Likely cause:** {stored.LikelyCause}\n\n**Suggested action:** {stored.SuggestedAction}\n\n**Draft ({stored.DraftLanguage}):**\n\n> {stored.DraftMessage.ReplaceLineEndings("\n> ")}\n\nScore: ?/5\n\n");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, report.ToString());
        Console.WriteLine();
        Console.WriteLine($"From the model: {fromModel}, template fallback: {fromTemplate}. Tokens: {promptTokens} prompt, {completionTokens} completion. Written to {output}.");
        return 0;
    }

    private static string? ValueOf(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
