using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Matching;
using ThreeWayMatch.Domain.Rules;
using ThreeWayMatch.Domain.Tests.Matching;
using static ThreeWayMatch.Domain.Tests.Rules.RuleTestSupport;

namespace ThreeWayMatch.Domain.Tests.Rules;

public class LineAndReferenceRulesTests
{
    private readonly MatchingWorld _world = new();

    // ---------- ITEM_NOT_ON_PO ----------

    [Fact]
    public void A_product_the_supplier_does_not_sell_is_not_on_the_order()
    {
        var invoice = TestInvoice.Board().Line("ZX-1234", "Sample set", 2m, "tk", 40m);

        var finding = Assess(invoice, _world).Single(DiscrepancyCode.ItemNotOnPo);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(2, finding.LineNumber);
        Assert.Equal(80m, finding.AmountAtStake);
    }

    [Fact]
    public void A_product_the_supplier_sells_but_this_order_does_not_contain_is_not_on_the_order()
    {
        // Lath exists in the catalogue; PO-2026-0002 only orders board.
        var invoice = TestInvoice.Board(5m).On("PO-2026-0002").Line("KS-0102", "Kuuseliist", 4m, "pakk", 100m);

        Assert.Contains(DiscrepancyCode.ItemNotOnPo, Assess(invoice, _world).Codes());
    }

    [Fact]
    public void A_surcharge_the_order_has_no_charge_line_for_is_not_on_the_order()
    {
        var invoice = TestInvoice.Board(5m).On("PO-2026-0002").Line("SURCH", "Fuel surcharge", 1m, "pcs", 60m);

        var finding = Assess(invoice, _world).Single(DiscrepancyCode.ItemNotOnPo);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(60m, finding.AmountAtStake);
    }

    [Fact]
    public void A_delivery_charge_the_order_does_contain_is_fine()
    {
        var invoice = TestInvoice.Board().Line("KS-0199", "Transport", 1m, "reis", 100m);

        Assert.DoesNotContain(DiscrepancyCode.ItemNotOnPo, Assess(invoice, _world).Codes());
    }

    [Fact]
    public void Lines_cannot_be_not_on_the_order_when_there_is_no_order_to_compare_with()
    {
        var invoice = TestInvoice.Board().On("PO-2026-9999").Line("ZX-1234", "Sample set", 2m, "tk", 40m);

        var codes = Assess(invoice, _world).Codes().ToList();

        Assert.DoesNotContain(DiscrepancyCode.ItemNotOnPo, codes);
        Assert.Contains(DiscrepancyCode.NoPoReference, codes);
    }

    // ---------- NO_PO_REFERENCE ----------

    [Fact]
    public void A_missing_reference_is_reported_and_the_invoice_goes_to_review()
    {
        var assessment = Assess(TestInvoice.Board(3m).On(null), _world);

        var finding = assessment.Single(DiscrepancyCode.NoPoReference);
        Assert.Equal("(blank)", finding.Actual);
        Assert.Equal(Decision.NeedsReview, assessment.Decision);
    }

    [Fact]
    public void An_unknown_reference_is_reported_with_what_was_printed()
    {
        var finding = Assess(TestInvoice.Board(3m).On("PO-2026-9999"), _world).Single(DiscrepancyCode.NoPoReference);

        Assert.Equal("PO-2026-9999", finding.Actual);
    }

    [Fact]
    public void A_reference_that_belongs_to_another_supplier_is_reported()
    {
        Assert.Contains(DiscrepancyCode.NoPoReference, Assess(TestInvoice.Board(3m).On("PO-2026-0006"), _world).Codes());
    }

    [Fact]
    public void A_suggested_order_still_needs_a_person_and_still_reports_the_missing_reference()
    {
        // Pine is only on PO-2026-0003, so the engine can suggest it, but a guess never approves a payment.
        var invoice = new TestInvoice().On(null).Line("KS-0103", "Männilaud 25x125 mm, hööveldatud", 12m, "m³", 100m);

        var assessment = Assess(invoice, _world);

        Assert.Equal(PurchaseOrderMatchMethod.SuggestedFromLines, assessment.Match.PurchaseOrder.Method);
        Assert.Contains(DiscrepancyCode.NoPoReference, assessment.Codes());
        Assert.Contains(assessment.Concerns, c => c.Kind == ConcernKind.PurchaseOrderSuggested);
        Assert.Equal(Decision.NeedsReview, assessment.Decision);
    }

    [Fact]
    public void A_correct_reference_reports_nothing()
    {
        Assert.DoesNotContain(DiscrepancyCode.NoPoReference, Assess(TestInvoice.Board(), _world).Codes());
    }

    [Fact]
    public void An_unknown_supplier_can_never_be_approved()
    {
        var invoice = TestInvoice.Board();
        invoice.Vat = "EE100000999";
        invoice.SupplierName = "Totally Different Ltd";

        var assessment = Assess(invoice, _world);

        Assert.Null(assessment.Match.Supplier.Supplier);
        Assert.Contains(DiscrepancyCode.NoPoReference, assessment.Codes());
        Assert.Equal(Decision.NeedsReview, assessment.Decision);
    }

    [Fact]
    public void A_wrong_vat_number_with_the_right_name_is_matched_by_name_and_must_be_reviewed()
    {
        var invoice = TestInvoice.Board();
        invoice.Vat = "EE100000999";

        var assessment = Assess(invoice, _world);

        Assert.Equal(SupplierMatchMethod.FuzzyName, assessment.Match.Supplier.Method);
        Assert.Contains(assessment.Concerns, c => c.Kind == ConcernKind.FuzzySupplier);
        Assert.Equal(Decision.NeedsReview, assessment.Decision);
    }

    // ---------- UOM_MISMATCH ----------

    [Fact]
    public void Stacked_cubic_metres_against_solid_ones_is_a_unit_mismatch()
    {
        var finding = Assess(new TestInvoice().Line("KS-0101", "Kuuselaud", 10m, "rm", 100m), _world).Single(DiscrepancyCode.UomMismatch);

        Assert.Equal("m³", finding.Expected);
        Assert.Equal("rm", finding.Actual);
        Assert.Equal(1000m, finding.AmountAtStake);
    }

    [Theory]
    [InlineData("furlongs")]
    [InlineData("")]
    public void A_unit_we_do_not_know_or_that_is_missing_is_reported(string unit)
    {
        var assessment = Assess(new TestInvoice().Line("KS-0101", "Kuuselaud", 10m, unit, 100m), _world);

        Assert.Contains(DiscrepancyCode.UomMismatch, assessment.Codes());
    }

    [Fact]
    public void A_pack_unit_and_a_base_unit_are_both_acceptable()
    {
        Assert.Empty(Assess(new TestInvoice().Line("KS-0102", "Kuuseliist", 2m, "pakk", 100m), _world).Findings);
        Assert.Empty(Assess(new TestInvoice().Line("KS-0102", "Kuuseliist", 0.5m, "m³", 400m), _world).Findings);
    }
}
