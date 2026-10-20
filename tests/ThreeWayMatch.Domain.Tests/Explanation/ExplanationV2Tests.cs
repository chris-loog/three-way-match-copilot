using Microsoft.EntityFrameworkCore;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Explanation;
using ThreeWayMatch.Domain.Tests.Pipeline;

namespace ThreeWayMatch.Domain.Tests.Explanation;

/// <summary>The v2 design: a code table, no supplier draft for some codes, and a letter frame written in C#.</summary>
public class ExplanationV2Tests
{
    private static ExplanationFacts Facts(string language, string? supplier, params FactFinding[] findings) => new(
        "Invoice", "A-261", supplier, language, "PO-2026-0045", "EUR", 5000m, "NeedsReview", 100m, findings, [], new SupplierHistory(0, new Dictionary<string, int>()));

    private static readonly FactFinding Totals = new("TOTALS_MISMATCH", "Warning", null, null, null, 5m);
    private static readonly FactFinding Partial = new("PARTIAL_DELIVERY", "Warning", 1, "9.24", "13.35", 100m);
    private static readonly FactFinding Bank = new("BANK_ACCOUNT_CHANGED", "Block", null, "EE1", "EE2", 5000m);

    private static ExplanationReply Body(string text = "Please confirm invoice A-261 for order PO-2026-0045.") =>
        new("Summary.", "Cause.", "Check it.", text);

    public static TheoryData<DiscrepancyCode> AllCodes() => new(Enum.GetValues<DiscrepancyCode>());

    [Theory]
    [MemberData(nameof(AllCodes))]
    public void Every_code_has_a_meaning_without_digits(DiscrepancyCode code)
    {
        var meaning = FindingCatalogue.Meaning(CodeNames.Of(code));

        Assert.NotEmpty(meaning);
        Assert.DoesNotContain(meaning, char.IsDigit);
    }

    [Fact]
    public void Only_a_bank_change_and_a_partial_delivery_forbid_a_message_to_the_supplier()
    {
        var forbidden = Enum.GetValues<DiscrepancyCode>().Select(CodeNames.Of).Where(c => !FindingCatalogue.AllowsSupplierDraft(c)).Order().ToList();

        Assert.Equal(["BANK_ACCOUNT_CHANGED", "PARTIAL_DELIVERY"], forbidden);
    }

    [Fact]
    public void A_partial_delivery_is_explained_as_goods_still_to_come_not_as_over_delivery()
    {
        var meaning = FindingCatalogue.Meaning("PARTIAL_DELIVERY");

        Assert.Contains("before all ordered goods were received", meaning, StringComparison.Ordinal);
        Assert.Contains("not over-delivery", meaning, StringComparison.Ordinal);
    }

    [Fact]
    public void The_facts_carry_each_findings_meaning_and_whether_a_draft_is_allowed()
    {
        var json = Facts("English", "Sillaotsa Saeveski OÜ", Partial).ToJson();

        Assert.Contains("\"meaning\":\"The invoice arrived before all ordered goods", json, StringComparison.Ordinal);
        Assert.Contains("\"supplierDraftAllowed\":false", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Any_forbidden_finding_drops_the_whole_draft_even_next_to_an_allowed_one()
    {
        var facts = Facts("English", "Sillaotsa Saeveski OÜ", Totals, Partial);

        var final = SupplierLetter.Finish(facts, Body());

        Assert.Equal(string.Empty, final.DraftMessageToSupplier);
    }

    [Fact]
    public void A_bank_change_tells_the_reviewer_to_phone_the_number_in_the_master_data()
    {
        var final = SupplierLetter.Finish(Facts("Estonian", "Sillaotsa Saeveski OÜ", Bank), Body());

        Assert.Equal(string.Empty, final.DraftMessageToSupplier);
        Assert.Contains("phone the supplier on the number in the supplier master data", final.SuggestedAction, StringComparison.Ordinal);
        Assert.StartsWith("Check it.", final.SuggestedAction, StringComparison.Ordinal);
    }

    [Fact]
    public void A_partial_delivery_tells_the_reviewer_to_wait_and_not_to_write()
    {
        var final = SupplierLetter.Finish(Facts("English", "Sillaotsa Saeveski OÜ", Partial), Body());

        Assert.Contains("Do not contact the supplier", final.SuggestedAction, StringComparison.Ordinal);
    }

    [Fact]
    public void The_fixed_instruction_is_added_once_per_code()
    {
        var final = SupplierLetter.Finish(Facts("English", "S OÜ", Partial, Partial with { Line = 2 }), Body());

        Assert.Equal(1, final.SuggestedAction.Split("Do not contact the supplier").Length - 1);
    }

    [Fact]
    public void The_estonian_letter_is_framed_with_the_supplier_name_from_the_facts_and_our_company()
    {
        var final = SupplierLetter.Finish(Facts("Estonian", "Sillaotsa Saeveski OÜ", Totals), Body("Palun kinnitage arve A-261."), "Näidis Puit OÜ");

        Assert.Equal("Lugupeetud Sillaotsa Saeveski OÜ,\n\nPalun kinnitage arve A-261.\n\nLugupidamisega,\nNäidis Puit OÜ ostuosakond", final.DraftMessageToSupplier);
    }

    [Fact]
    public void The_english_letter_is_framed_and_a_missing_supplier_gets_a_neutral_greeting()
    {
        var named = SupplierLetter.Finish(Facts("English", "Nordic Pack Solutions OÜ", Totals), Body("Please confirm."), "Näidis Puit OÜ");
        var unnamed = SupplierLetter.Finish(Facts("English", null, Totals), Body("Please confirm."), "Näidis Puit OÜ");

        Assert.Equal("Dear Nordic Pack Solutions OÜ,\n\nPlease confirm.\n\nKind regards,\nNäidis Puit OÜ purchasing", named.DraftMessageToSupplier);
        Assert.StartsWith("Dear Sir or Madam,", unnamed.DraftMessageToSupplier, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Dear [Name], please confirm.")]
    [InlineData("Please confirm. [Your name]")]
    public void A_placeholder_in_square_brackets_fails_the_guard(string body)
    {
        var failures = ExplanationGuards.Check(
            ExplanationGuardTests.Facts("English", new FactFinding("LOW_CONFIDENCE", "Warning", null, null, null, 0m)),
            new ExplanationReply("LOW_CONFIDENCE.", "Unclear.", "Check it.", body));

        Assert.Contains(failures, f => f.Contains("placeholder", StringComparison.Ordinal));
    }

    [Fact]
    public void The_schema_without_a_draft_has_no_draft_field_and_parsing_drops_a_draft_the_model_wrote_anyway()
    {
        Assert.DoesNotContain("draft_message_to_supplier", ExplanationReply.SchemaFor(withDraft: false), StringComparison.Ordinal);
        Assert.Contains("draft_message_to_supplier", ExplanationReply.SchemaFor(withDraft: true), StringComparison.Ordinal);

        var withoutField = ExplanationReply.TryParse("""{"summary":"a","likely_cause":"b","suggested_action":"c"}""", draftExpected: false);
        var withField = ExplanationReply.TryParse("""{"summary":"a","likely_cause":"b","suggested_action":"c","draft_message_to_supplier":"Dear X"}""", draftExpected: false);

        Assert.NotNull(withoutField);
        Assert.Equal(string.Empty, withField!.DraftMessageToSupplier);
        Assert.Null(ExplanationReply.TryParse("""{"summary":"a","likely_cause":"b","suggested_action":"c"}""", draftExpected: true));
    }

    [Fact]
    public async Task A_document_on_hold_for_missing_goods_is_stored_without_a_draft_and_asked_for_without_one()
    {
        using var world = new PipelineWorld(received: false);
        using (var db = world.NewContext())
        {
            var status = await PipelineWorld.NewProcessor(db, new FakeExtractor(PipelineWorld.CleanResult()))
                .ProcessAsync("incoming/invoices/a.pdf", DocumentType.Invoice, [1, 2, 3], CancellationToken.None);
            Assert.Equal(DocumentStatus.OnHold, status);
        }

        var llm = new FakeLlm((prompt, _) => FakeLlm.Good(prompt));
        using var db2 = world.NewContext();
        var id = await db2.Documents.Select(d => d.Id).SingleAsync();

        var outcome = await new ThreeWayMatch.Infrastructure.Explanation.ExplanationService(
            db2, llm, TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<ThreeWayMatch.Infrastructure.Explanation.ExplanationService>.Instance)
            .GenerateAsync(id, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.DoesNotContain("draft_message_to_supplier", llm.Schemas.Single(), StringComparison.Ordinal);
        Assert.Contains("\"supplierDraftAllowed\":false", llm.Prompts.Single(), StringComparison.Ordinal);
        var stored = await world.NewContext().Explanations.SingleAsync();
        Assert.Equal(string.Empty, stored.DraftMessage);
        Assert.Contains("Do not contact the supplier", stored.SuggestedAction, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stopped_invoice_gets_a_framed_letter_with_our_company_from_the_options()
    {
        using var world = new PipelineWorld();
        using (var db = world.NewContext())
        {
            await PipelineWorld.NewProcessor(db, new FakeExtractor(PipelineWorld.SmudgedResult()))
                .ProcessAsync("incoming/invoices/a.pdf", DocumentType.Invoice, [1, 2, 3], CancellationToken.None);
        }

        var llm = new FakeLlm((prompt, _) => FakeLlm.Good(prompt));
        using var db2 = world.NewContext();
        var id = await db2.Documents.Select(d => d.Id).SingleAsync();
        var options = new ThreeWayMatch.Infrastructure.Explanation.ExplanationOptions { CompanyName = "Test Company OÜ" };

        await new ThreeWayMatch.Infrastructure.Explanation.ExplanationService(
            db2, llm, TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<ThreeWayMatch.Infrastructure.Explanation.ExplanationService>.Instance, options)
            .GenerateAsync(id, CancellationToken.None);

        var stored = await world.NewContext().Explanations.SingleAsync();
        Assert.StartsWith("Dear Baltic Board Supply", stored.DraftMessage, StringComparison.Ordinal);
        Assert.EndsWith("Test Company OÜ purchasing", stored.DraftMessage, StringComparison.Ordinal);
        Assert.Contains("draft_message_to_supplier", llm.Schemas.Single(), StringComparison.Ordinal);
    }
}
