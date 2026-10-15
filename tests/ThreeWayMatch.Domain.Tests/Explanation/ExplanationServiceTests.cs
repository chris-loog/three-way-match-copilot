using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Explanation;
using ThreeWayMatch.Domain.Tests.Pipeline;
using ThreeWayMatch.Infrastructure.Explanation;
using ThreeWayMatch.Infrastructure.Persistence;

namespace ThreeWayMatch.Domain.Tests.Explanation;

/// <summary>Stands in for Azure OpenAI: answers from a function of the prompt, or throws, and records every prompt.</summary>
internal sealed class FakeLlm(Func<string, int, LlmResult> answer) : ILlmClient
{
    public List<string> Prompts { get; } = [];

    public Task<LlmResult> CompleteJsonAsync(string systemPrompt, string userPrompt, string schemaName, string jsonSchema, CancellationToken cancellationToken)
    {
        Prompts.Add(userPrompt);
        return Task.FromResult(answer(userPrompt, Prompts.Count));
    }

    /// <summary>An answer that names every finding code in the facts it was given, so it passes the guards.</summary>
    public static LlmResult Good(string prompt, string? extra = null)
    {
        var json = prompt[(prompt.IndexOf("<facts>", StringComparison.Ordinal) + 7)..prompt.IndexOf("</facts>", StringComparison.Ordinal)];
        var codes = JsonDocument.Parse(json).RootElement.GetProperty("findings").EnumerateArray().Select(f => f.GetProperty("code").GetString()).ToList();
        var reply = new
        {
            summary = $"Stopped because of {string.Join(" and ", codes)}.{extra}",
            likely_cause = "The cause is unclear from the facts.",
            suggested_action = "Check the findings against the order and the receipt.",
            draft_message_to_supplier = "Hello, please confirm the document or send a corrected one.",
        };
        return new LlmResult(JsonSerializer.Serialize(reply), 300, 120, "gpt-4.1-mini-2025-04-14");
    }
}

public class ExplanationServiceTests
{
    private static readonly byte[] Pdf = [1, 2, 3];

    private static async Task<(PipelineWorld World, int DocumentId)> NeedsReviewDocument(string raw)
    {
        var world = new PipelineWorld();
        using var db = world.NewContext();
        var status = await PipelineWorld.NewProcessor(db, new FakeExtractor(raw)).ProcessAsync("incoming/invoices/a.pdf", DocumentType.Invoice, Pdf, CancellationToken.None);
        Assert.Equal(DocumentStatus.NeedsReview, status);
        return (world, await db.Documents.Select(d => d.Id).SingleAsync());
    }

    private static ExplanationService Service(ThreeWayMatchDbContext db, ILlmClient llm) =>
        new(db, llm, TimeProvider.System, NullLogger<ExplanationService>.Instance);

    [Fact]
    public async Task A_good_answer_is_stored_with_its_token_usage()
    {
        var (world, id) = await NeedsReviewDocument(PipelineWorld.SmudgedResult());
        using var _ = world;
        var llm = new FakeLlm((prompt, _) => FakeLlm.Good(prompt));
        using var db = world.NewContext();

        var outcome = await Service(db, llm).GenerateAsync(id, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        var stored = await world.NewContext().Explanations.SingleAsync();
        Assert.Equal(ExplanationSource.Model, stored.Source);
        Assert.Equal("gpt-4.1-mini-2025-04-14", stored.ModelId);
        Assert.Contains("LOW_CONFIDENCE", stored.Summary, StringComparison.Ordinal);
        Assert.Equal(300, stored.PromptTokens);
        Assert.Equal(120, stored.CompletionTokens);
        Assert.Equal("English", stored.DraftLanguage);
        Assert.Null(stored.GuardNotes);
    }

    [Fact]
    public async Task The_model_gets_the_facts_and_never_the_raw_document_result()
    {
        var (world, id) = await NeedsReviewDocument(PipelineWorld.SmudgedResult());
        using var _ = world;
        var llm = new FakeLlm((prompt, _) => FakeLlm.Good(prompt));
        using var db = world.NewContext();

        await Service(db, llm).GenerateAsync(id, CancellationToken.None);

        var prompt = Assert.Single(llm.Prompts);
        Assert.Contains("\"findings\"", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("boundingRegions", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("analyzeResult", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("EE780400150311134947", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_answer_that_fails_the_guards_is_retried_once_with_the_reasons()
    {
        var (world, id) = await NeedsReviewDocument(PipelineWorld.SmudgedResult());
        using var _ = world;
        var llm = new FakeLlm((prompt, call) => call == 1 ? FakeLlm.Good(prompt, " It costs 999999.99 EUR.") : FakeLlm.Good(prompt));
        using var db = world.NewContext();

        var outcome = await Service(db, llm).GenerateAsync(id, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(2, llm.Prompts.Count);
        Assert.Contains("rejected", llm.Prompts[1], StringComparison.Ordinal);
        Assert.Contains("999999.99", llm.Prompts[1], StringComparison.Ordinal);
        var stored = await world.NewContext().Explanations.SingleAsync();
        Assert.Equal(ExplanationSource.Model, stored.Source);
        Assert.Equal(600, stored.PromptTokens);
    }

    [Fact]
    public async Task Two_bad_answers_give_the_template_text_and_keep_the_reasons()
    {
        var (world, id) = await NeedsReviewDocument(PipelineWorld.SmudgedResult());
        using var _ = world;
        var llm = new FakeLlm((prompt, _) => FakeLlm.Good(prompt, " It costs 999999.99 EUR."));
        using var db = world.NewContext();

        var outcome = await Service(db, llm).GenerateAsync(id, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        var stored = await world.NewContext().Explanations.SingleAsync();
        Assert.Equal(ExplanationSource.Template, stored.Source);
        Assert.Null(stored.ModelId);
        Assert.Contains("999999.99", stored.GuardNotes, StringComparison.Ordinal);
        Assert.Contains("LOW_CONFIDENCE", stored.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreachable_model_gives_the_template_text_without_failing()
    {
        var (world, id) = await NeedsReviewDocument(PipelineWorld.SmudgedResult());
        using var _ = world;
        var llm = new FakeLlm((_, _) => throw new HttpRequestException("down"));
        using var db = world.NewContext();

        var outcome = await Service(db, llm).GenerateAsync(id, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        var stored = await world.NewContext().Explanations.SingleAsync();
        Assert.Equal(ExplanationSource.Template, stored.Source);
        Assert.Contains("could not be reached", stored.GuardNotes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Asking_again_replaces_the_stored_explanation()
    {
        var (world, id) = await NeedsReviewDocument(PipelineWorld.SmudgedResult());
        using var _ = world;
        var llm = new FakeLlm((prompt, _) => FakeLlm.Good(prompt));
        using var db = world.NewContext();
        var service = Service(db, llm);

        await service.GenerateAsync(id, CancellationToken.None);
        await service.GenerateAsync(id, CancellationToken.None);

        Assert.Equal(1, await world.NewContext().Explanations.CountAsync());
    }

    [Fact]
    public async Task A_document_that_needs_no_decision_gets_no_explanation_and_no_model_call()
    {
        using var world = new PipelineWorld();
        using var db = world.NewContext();
        await PipelineWorld.NewProcessor(db, new FakeExtractor(PipelineWorld.CleanResult())).ProcessAsync("incoming/invoices/a.pdf", DocumentType.Invoice, Pdf, CancellationToken.None);
        var llm = new FakeLlm((prompt, _) => FakeLlm.Good(prompt));

        var outcome = await Service(db, llm).GenerateAsync(await db.Documents.Select(d => d.Id).SingleAsync(), CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Empty(llm.Prompts);
        Assert.Equal(0, await db.Explanations.CountAsync());
    }

    [Fact]
    public async Task A_new_match_removes_the_stale_explanation()
    {
        var (world, id) = await NeedsReviewDocument(PipelineWorld.SmudgedResult());
        using var _ = world;
        using var db = world.NewContext();
        await Service(db, new FakeLlm((prompt, _) => FakeLlm.Good(prompt))).GenerateAsync(id, CancellationToken.None);

        using var again = world.NewContext();
        await PipelineWorld.NewMatcher(again).MatchAsync(id, CancellationToken.None);

        Assert.Equal(0, await world.NewContext().Explanations.CountAsync());
    }
}
