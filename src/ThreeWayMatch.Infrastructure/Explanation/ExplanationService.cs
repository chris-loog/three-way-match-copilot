using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Explanation;
using ThreeWayMatch.Infrastructure.Persistence;

namespace ThreeWayMatch.Infrastructure.Explanation;

public sealed record ExplanationOutcome(bool Succeeded, string Message);

/// <summary>
/// Explains why a document was stopped. Builds the facts, asks the model, checks the answer with the guards, retries once,
/// and falls back to the template text. The model only explains: nothing here changes a decision.
/// </summary>
public sealed partial class ExplanationService(
    ThreeWayMatchDbContext db,
    ILlmClient llm,
    TimeProvider clock,
    ILogger<ExplanationService> logger,
    ExplanationOptions? options = null)
{
    private readonly string company = options?.CompanyName ?? SupplierLetter.DefaultCompany;

    private const int MaxAttempts = 2;

    public async Task<ExplanationOutcome> GenerateAsync(int documentId, CancellationToken cancellationToken)
    {
        var document = await db.Documents.AsNoTracking()
            .Include(d => d.Extracted).ThenInclude(e => e!.Lines)
            .Include(d => d.MatchResult).ThenInclude(m => m!.Supplier)
            .Include(d => d.MatchResult).ThenInclude(m => m!.PurchaseOrder)
            .Include(d => d.MatchResult).ThenInclude(m => m!.Discrepancies)
            .Include(d => d.MatchResult).ThenInclude(m => m!.LineMatches)
            .AsSplitQuery()
            .SingleOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (document?.MatchResult is not { Decision: Decision.NeedsReview or Decision.OnHold, ReviewedAt: null } match || document.Extracted is null)
        {
            return new ExplanationOutcome(false, "Only a document that is waiting for a decision gets an explanation.");
        }

        var facts = await BuildFactsAsync(document, match, cancellationToken);
        var (reply, source, notes, usage) = await AskAsync(facts, cancellationToken);

        var existing = await db.Explanations.FirstOrDefaultAsync(e => e.DocumentId == documentId, cancellationToken);
        var explanation = existing ?? new MatchExplanation
        {
            DocumentId = documentId, Summary = string.Empty, LikelyCause = string.Empty, SuggestedAction = string.Empty, DraftMessage = string.Empty, DraftLanguage = string.Empty,
        };

        explanation.Source = source;
        explanation.ModelId = source == ExplanationSource.Model ? usage.Model : null;
        explanation.Summary = reply.Summary;
        explanation.LikelyCause = reply.LikelyCause;
        explanation.SuggestedAction = reply.SuggestedAction;
        explanation.DraftMessage = reply.DraftMessageToSupplier;
        explanation.DraftLanguage = facts.SupplierLanguage;
        explanation.GuardNotes = notes;
        explanation.PromptTokens = usage.PromptTokens;
        explanation.CompletionTokens = usage.CompletionTokens;
        explanation.GeneratedAt = clock.GetUtcNow();

        if (existing is null)
        {
            db.Explanations.Add(explanation);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new ExplanationOutcome(true, source == ExplanationSource.Model ? "Explanation written." : "The model's answer could not be used; showing the standard text.");
    }

    private async Task<(ExplanationReply Reply, ExplanationSource Source, string? Notes, LlmResult Usage)> AskAsync(ExplanationFacts facts, CancellationToken cancellationToken)
    {
        IReadOnlyList<string>? failures = null;
        var total = new LlmResult(string.Empty, 0, 0, string.Empty);
        var notes = new List<string>();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var timer = Stopwatch.StartNew();
            LlmResult result;
            try
            {
                result = await llm.CompleteJsonAsync(ExplanationPrompt.System, ExplanationPrompt.User(facts, failures), ExplanationReply.SchemaName, ExplanationReply.SchemaFor(facts.SupplierDraftAllowed), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogCallFailed(logger, ex, attempt);
                notes.Add($"Attempt {attempt}: the model could not be reached ({ex.GetType().Name}).");
                break;
            }

            total = new LlmResult(result.Text, total.PromptTokens + result.PromptTokens, total.CompletionTokens + result.CompletionTokens, result.Model);
            LogUsage(logger, attempt, result.Model, result.PromptTokens, result.CompletionTokens, timer.ElapsedMilliseconds);

            var reply = ExplanationReply.TryParse(result.Text, facts.SupplierDraftAllowed);
            failures = reply is null ? ["The answer was not valid JSON with all required fields."] : ExplanationGuards.Check(facts, reply);
            if (reply is not null && failures.Count == 0)
            {
                return (SupplierLetter.Finish(facts, reply, company), ExplanationSource.Model, attempt > 1 ? $"Attempt 1 failed the checks and was retried." : null, total);
            }

            notes.Add($"Attempt {attempt}: {string.Join(' ', failures)}");
        }

        LogFallback(logger, string.Join(' ', notes));
        return (SupplierLetter.Finish(facts, ExplanationFallback.Build(facts), company), ExplanationSource.Template, string.Join(' ', notes), total);
    }

    private async Task<ExplanationFacts> BuildFactsAsync(Document document, MatchResult match, CancellationToken cancellationToken)
    {
        var extracted = document.Extracted!;
        var lineNumbers = extracted.Lines.ToDictionary(l => l.Id, l => l.LineNumber);
        var lineMatchNumbers = match.LineMatches.ToDictionary(m => m.Id, m => lineNumbers.GetValueOrDefault(m.ExtractedLineId));

        var findings = match.Discrepancies
            .OrderByDescending(d => d.Severity).ThenByDescending(d => d.AmountAtStake)
            .Select(d => new FactFinding(
                CodeNames.Of(d.Code), d.Severity.ToString(), d.LineMatchId is { } id ? lineMatchNumbers.GetValueOrDefault(id) : null,
                ExplanationFacts.Clean(d.ExpectedValue, 80), ExplanationFacts.Clean(d.ActualValue, 80), d.AmountAtStake))
            .ToList();

        var methods = match.LineMatches.ToDictionary(m => m.ExtractedLineId, m => m.Method.ToString());
        var lines = extracted.Lines.OrderBy(l => l.LineNumber)
            .Select(l => new FactLine(l.LineNumber, ExplanationFacts.Clean(l.Description), l.Quantity, l.Unit, l.UnitPrice, methods.GetValueOrDefault(l.Id, nameof(MatchMethod.None))))
            .ToList();

        return new ExplanationFacts(
            document.Type == DocumentType.Invoice ? "Invoice" : "Delivery note",
            extracted.DocumentNumber,
            match.Supplier?.Name ?? extracted.SupplierName,
            ExplanationFacts.Language(match.Supplier?.DocumentCulture),
            match.PurchaseOrder?.Number,
            extracted.Currency,
            extracted.GrossTotal,
            match.Decision.ToString(),
            match.AmountAtStake,
            findings,
            lines,
            await HistoryAsync(document, match.SupplierId, cancellationToken));
    }

    /// <summary>What this supplier has sent in the last six months and which findings its earlier documents had.</summary>
    private async Task<SupplierHistory> HistoryAsync(Document document, int? supplierId, CancellationToken cancellationToken)
    {
        if (supplierId is null)
        {
            return new SupplierHistory(0, new Dictionary<string, int>());
        }

        var since = clock.GetUtcNow().AddMonths(-6);
        var invoices = await db.Documents.AsNoTracking()
            .CountAsync(d => d.Id != document.Id && d.Type == DocumentType.Invoice && d.ReceivedAt >= since && d.MatchResult != null && d.MatchResult.SupplierId == supplierId, cancellationToken);

        var earlier = await db.Discrepancies.AsNoTracking()
            .Where(x => x.MatchResult!.DocumentId != document.Id && x.MatchResult.SupplierId == supplierId)
            .GroupBy(x => x.Code)
            .Select(g => new { Code = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return new SupplierHistory(invoices, earlier.ToDictionary(e => CodeNames.Of(e.Code), e => e.Count));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Explanation attempt {Attempt} with {Model}: {PromptTokens} prompt and {CompletionTokens} completion tokens in {ElapsedMs} ms.")]
    private static partial void LogUsage(ILogger logger, int attempt, string model, int promptTokens, int completionTokens, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The model call failed on attempt {Attempt}; using the standard text.")]
    private static partial void LogCallFailed(ILogger logger, Exception exception, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The model's answer was not used: {Notes}")]
    private static partial void LogFallback(ILogger logger, string notes);
}
