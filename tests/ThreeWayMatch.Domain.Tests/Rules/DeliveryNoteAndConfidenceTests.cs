using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Domain.Matching;
using ThreeWayMatch.Domain.Rules;
using ThreeWayMatch.Domain.Tests.Matching;
using static ThreeWayMatch.Domain.Tests.Rules.RuleTestSupport;

namespace ThreeWayMatch.Domain.Tests.Rules;

public class DeliveryNoteAndConfidenceTests
{
    private readonly MatchingWorld _world = new();

    private Assessment Note(string number, params MatchInputLine[] lines) =>
        DocumentAssessor.CreateDefault().AssessDeliveryNote(
            new MatchInput(MatchingWorld.TimberVat, null, "PO-2026-0001", lines), number, [], _world.Snapshot());

    // ---------- DELIVERY_NOTE_MISMATCH ----------

    [Fact]
    public void A_note_that_agrees_with_what_the_warehouse_booked_is_fine()
    {
        var assessment = Note("DN-1001", MatchingWorld.Line(1, "KS-0101", null, 10m, "m³"));

        Assert.Empty(assessment.Findings);
        Assert.Equal(Decision.AutoApproved, assessment.Decision);
    }

    [Fact]
    public void A_note_that_differs_from_the_booked_quantity_is_reported_even_by_a_little()
    {
        var assessment = Note("DN-1001", MatchingWorld.Line(1, "KS-0101", null, 9m, "m³"));

        var finding = assessment.Single(DiscrepancyCode.DeliveryNoteMismatch);
        Assert.Equal("10", finding.Expected);
        Assert.Equal("9", finding.Actual);
        Assert.Equal(100m, finding.AmountAtStake);
        Assert.Equal(Decision.NeedsReview, assessment.Decision);
    }

    [Fact]
    public void A_note_is_compared_in_the_base_unit_so_packs_work()
    {
        // 8 packs of 0.25 m³ were booked.
        Assert.Empty(Note("DN-1001", MatchingWorld.Line(1, "KS-0102", null, 8m, "pakk")).Findings);

        var finding = Note("DN-1001", MatchingWorld.Line(1, "KS-0102", null, 7m, "pakk")).Single(DiscrepancyCode.DeliveryNoteMismatch);
        Assert.Equal("2", finding.Expected);
        Assert.Equal("1.75", finding.Actual);
        Assert.Equal(100m, finding.AmountAtStake); // one pack at 100
    }

    [Fact]
    public void A_note_whose_receipt_is_not_booked_yet_is_not_compared_because_arrival_order_must_not_matter()
    {
        var assessment = Note("DN-9999", MatchingWorld.Line(1, "KS-0101", null, 9m, "m³"));

        Assert.Empty(assessment.Findings);
    }

    [Fact]
    public void The_note_number_is_matched_however_it_is_written()
    {
        Assert.Contains(DiscrepancyCode.DeliveryNoteMismatch, Note("dn 1001", MatchingWorld.Line(1, "KS-0101", null, 9m, "m³")).Codes());
    }

    [Fact]
    public void Invoice_rules_do_not_run_on_delivery_notes()
    {
        // A price is not on a note, and "not on the order" or a missing reference are invoice matters.
        var assessment = Note("DN-1001", MatchingWorld.Line(1, "KS-0101", null, 10m, "m³"), MatchingWorld.Line(2, "ZX-1", "Sample set", 1m, "tk"));

        Assert.DoesNotContain(DiscrepancyCode.ItemNotOnPo, assessment.Codes());
    }

    [Fact]
    public void A_unit_that_cannot_be_converted_on_a_note_is_a_unit_problem_not_a_quantity_problem()
    {
        var assessment = Note("DN-1001", MatchingWorld.Line(1, "KS-0101", null, 10m, "rm"));

        Assert.Equal([DiscrepancyCode.UomMismatch], assessment.Codes());
    }

    // ---------- LOW_CONFIDENCE ----------

    [Fact]
    public void A_key_field_below_the_threshold_is_reported_by_name()
    {
        var invoice = TestInvoice.Board().Build() with
        {
            PurchaseOrderReference = new ExtractedField<string>("PO-2026-0001", "PO-2026-0001", 0.5),
        };

        var finding = Assess(invoice, _world).Single(DiscrepancyCode.LowConfidence);

        Assert.Contains("PurchaseOrderReference", finding.Actual);
    }

    [Fact]
    public void The_threshold_is_configurable()
    {
        var invoice = TestInvoice.Board();

        Assert.DoesNotContain(DiscrepancyCode.LowConfidence, Assess(invoice, _world).Codes());
        Assert.Contains(DiscrepancyCode.LowConfidence, Assess(invoice, _world, options: new DiscrepancyOptions { ConfidenceThreshold = 0.99 }).Codes());
    }

    [Fact]
    public void An_invoice_with_no_lines_is_unreadable_and_must_not_pass()
    {
        var invoice = TestInvoice.Board().Build() with { Lines = [] };

        var assessment = Assess(invoice, _world);

        Assert.Contains("Lines", assessment.Single(DiscrepancyCode.LowConfidence).Actual);
        Assert.Equal(Decision.NeedsReview, assessment.Decision);
    }

    [Fact]
    public void A_value_the_model_and_our_own_reading_disagree_on_is_unreliable()
    {
        // The model says 1.234 for the gross total; on an Estonian invoice the printed "1.234" means 1234.
        var invoice = TestInvoice.Board().Build() with { GrossTotal = new ExtractedField<decimal?>(1.234m, "1.234", 0.95) };

        var assessment = Assess(invoice, _world);

        Assert.Contains("GrossTotal", assessment.Single(DiscrepancyCode.LowConfidence).Actual);
    }

    [Fact]
    public void A_delivery_note_uses_the_confidence_supplied_by_the_caller()
    {
        var assessment = DocumentAssessor.CreateDefault().AssessDeliveryNote(
            new MatchInput(MatchingWorld.TimberVat, null, "PO-2026-0001", [MatchingWorld.Line(1, "KS-0101", null, 10m, "m³")]),
            "DN-1001", ["Line1.Quantity"], _world.Snapshot());

        Assert.Contains("Line1.Quantity", assessment.Single(DiscrepancyCode.LowConfidence).Actual);
    }
}
