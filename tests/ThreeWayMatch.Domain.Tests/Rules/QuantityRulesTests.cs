using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Erp;
using ThreeWayMatch.Domain.Rules;
using ThreeWayMatch.Domain.Tests.Matching;
using static ThreeWayMatch.Domain.Tests.Rules.RuleTestSupport;

namespace ThreeWayMatch.Domain.Tests.Rules;

public class QuantityRulesTests
{
    private readonly MatchingWorld _world = new();

    [Fact]
    public void Invoicing_exactly_what_arrived_is_fine()
    {
        Assert.Empty(Assess(TestInvoice.Board(10m), _world).Findings);
    }

    [Theory]
    [InlineData(10.2, false)] // 2 % tolerance
    [InlineData(10.21, true)]
    [InlineData(12, true)]
    public void Invoicing_more_than_arrived_is_over_invoicing_beyond_the_tolerance(double quantity, bool expected)
    {
        var assessment = Assess(TestInvoice.Board((decimal)quantity), _world);

        Assert.Equal(expected, assessment.Codes().Contains(DiscrepancyCode.QtyOverInvoiced));
    }

    [Theory]
    [InlineData(15, Severity.Warning)] // 5 m³ too many x 100 = 500, not above the threshold
    [InlineData(16, Severity.Block)] // 600
    public void Over_invoicing_becomes_a_block_above_the_euro_threshold(int quantity, Severity expected)
    {
        var finding = Assess(TestInvoice.Board(quantity), _world).Single(DiscrepancyCode.QtyOverInvoiced);

        Assert.Equal(expected, finding.Severity);
        Assert.Equal((quantity - 10) * 100m, finding.AmountAtStake);
    }

    [Fact]
    public void Quantity_already_invoiced_counts_against_what_may_still_be_invoiced()
    {
        // 4 of the 10 m³ are already invoiced, so only 6 remain.
        var snapshot = _world.Snapshot(invoiced: new Dictionary<int, decimal> { [11] = 4m });

        Assert.Empty(Assess(TestInvoice.Board(6m), _world, snapshot: snapshot).Findings);
        Assert.Contains(DiscrepancyCode.QtyOverInvoiced, Assess(TestInvoice.Board(7m), _world, snapshot: snapshot).Codes());
    }

    [Fact]
    public void Several_lines_for_one_item_are_added_together_before_the_check()
    {
        var invoice = TestInvoice.Board(6m).Line("KS-0101", "Kuuselaud 25x150 mm, kuivatatud, 4,8 m", 6m, "m³", 100m);

        var findings = Assess(invoice, _world).Findings.Where(f => f.Code == DiscrepancyCode.QtyOverInvoiced).ToList();

        Assert.Single(findings);
        Assert.Equal(200m, findings[0].AmountAtStake);
    }

    [Fact]
    public void A_supplier_can_have_its_own_quantity_tolerance()
    {
        _world.Timber.QuantityTolerancePercent = 5m;

        Assert.DoesNotContain(DiscrepancyCode.QtyOverInvoiced, Assess(TestInvoice.Board(10.4m), _world).Codes());
        Assert.Contains(DiscrepancyCode.QtyOverInvoiced, Assess(TestInvoice.Board(10.6m), _world).Codes());
    }

    [Fact]
    public void Packs_are_checked_in_the_base_unit()
    {
        // 8 packs arrived (2 m³). 8 is fine, 9 packs is early but within the order (still expected), so it waits.
        Assert.Empty(Assess(new TestInvoice().Line("KS-0102", "Kuuseliist", 8m, "pakk", 100m), _world).Findings);
        Assert.Equal([DiscrepancyCode.PartialDelivery], Assess(new TestInvoice().Line("KS-0102", "Kuuseliist", 9m, "pakk", 100m), _world).Codes());
    }

    [Fact]
    public void More_than_was_ordered_is_over_invoicing_even_while_goods_are_still_expected()
    {
        // Lath: 20 packs ordered, 8 arrived. 25 packs exceeds the order, so waiting would not make it right.
        var assessment = Assess(new TestInvoice().Line("KS-0102", "Kuuseliist", 25m, "pakk", 100m), _world);

        Assert.Contains(DiscrepancyCode.QtyOverInvoiced, assessment.Codes());
        Assert.DoesNotContain(DiscrepancyCode.PartialDelivery, assessment.Codes());
    }

    [Fact]
    public void A_manually_closed_line_never_waits_for_goods()
    {
        _world.Po1.Lines[1].Status = PurchaseOrderLineStatus.Closed; // short delivery accepted

        var assessment = Assess(new TestInvoice().Line("KS-0102", "Kuuseliist", 20m, "pakk", 100m), _world);

        Assert.Contains(DiscrepancyCode.QtyOverInvoiced, assessment.Codes());
        Assert.DoesNotContain(DiscrepancyCode.PartialDelivery, assessment.Codes());
    }

    [Fact]
    public void An_order_with_nothing_received_yet_is_a_wait_not_an_error()
    {
        // PO-2026-0002 has 5 m³ of board and no receipt.
        var assessment = Assess(TestInvoice.Board(5m).On("PO-2026-0002"), _world);

        Assert.Equal(Decision.OnHold, assessment.Decision);
        Assert.Equal([DiscrepancyCode.PartialDelivery], assessment.Codes());
        Assert.Equal(500m, assessment.Single(DiscrepancyCode.PartialDelivery).AmountAtStake);
    }

    [Fact]
    public void A_line_with_an_unconvertible_unit_is_left_to_the_unit_rule()
    {
        var assessment = Assess(new TestInvoice().Line("KS-0101", "Kuuselaud", 10m, "rm", 100m), _world);

        Assert.DoesNotContain(DiscrepancyCode.QtyOverInvoiced, assessment.Codes());
        Assert.Contains(DiscrepancyCode.UomMismatch, assessment.Codes());
    }
}
