using ThreeWayMatch.Domain.Matching;
using static ThreeWayMatch.Domain.Tests.Matching.MatchingWorld;

namespace ThreeWayMatch.Domain.Tests.Matching;

public class PurchaseOrderMatchingTests
{
    private readonly MatchingWorld _world = new();

    [Theory]
    [InlineData("PO-2026-0001")]
    [InlineData("po 2026 0001")]
    [InlineData("PO-2026-0001.")]
    [InlineData("PO20260001")]
    public void Finds_the_order_by_its_printed_reference_however_it_is_written(string printed)
    {
        var outcome = MatchingEngine.Match(Invoice(TimberVat, printed, Line(1, "KS-0101", null, 5m, "m³")), _world.Snapshot());

        Assert.Same(_world.Po1, outcome.PurchaseOrder.PurchaseOrder);
        Assert.Equal(PurchaseOrderMatchMethod.Reference, outcome.PurchaseOrder.Method);
        Assert.Equal(PurchaseOrderReferenceStatus.Matched, outcome.PurchaseOrder.ReferenceStatus);
        Assert.Empty(outcome.Concerns);
    }

    [Fact]
    public void A_reference_that_belongs_to_another_supplier_is_not_used()
    {
        // PO-2026-0006 exists, but it is the panel supplier's order.
        var outcome = MatchingEngine.Match(Invoice(TimberVat, "PO-2026-0006", Line(1, "KS-0103", null, 5m, "m³")), _world.Snapshot());

        Assert.Equal(PurchaseOrderReferenceStatus.BelongsToOtherSupplier, outcome.PurchaseOrder.ReferenceStatus);
        Assert.NotSame(_world.Po6, outcome.PurchaseOrder.PurchaseOrder);
    }

    [Fact]
    public void An_unknown_reference_is_reported_and_the_search_for_a_fitting_order_still_runs()
    {
        var outcome = MatchingEngine.Match(Invoice(TimberVat, "PO-2026-9999", Line(1, "KS-0103", null, 5m, "m³")), _world.Snapshot());

        Assert.Equal(PurchaseOrderReferenceStatus.Unknown, outcome.PurchaseOrder.ReferenceStatus);
        Assert.Same(_world.Po3, outcome.PurchaseOrder.PurchaseOrder);
        Assert.Equal(PurchaseOrderMatchMethod.SuggestedFromLines, outcome.PurchaseOrder.Method);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_reference_with_exactly_one_fitting_order_is_a_suggestion_that_needs_review(string? printed)
    {
        // Pine is only on PO-2026-0003.
        var outcome = MatchingEngine.Match(Invoice(TimberVat, printed, Line(1, "KS-0103", null, 12m, "m³")), _world.Snapshot());

        Assert.Same(_world.Po3, outcome.PurchaseOrder.PurchaseOrder);
        Assert.Equal(PurchaseOrderMatchMethod.SuggestedFromLines, outcome.PurchaseOrder.Method);
        Assert.Equal(PurchaseOrderReferenceStatus.Missing, outcome.PurchaseOrder.ReferenceStatus);
        Assert.Contains(outcome.Concerns, c => c.Kind == ConcernKind.PurchaseOrderSuggested);
        Assert.Same(_world.Po3.Lines[0], outcome.Lines[0].PurchaseOrderLine);
    }

    [Fact]
    public void A_missing_reference_with_several_fitting_orders_picks_none()
    {
        // 3 m³ of board fits PO-1, PO-2, PO-4 and PO-5.
        var outcome = MatchingEngine.Match(Invoice(TimberVat, null, Line(1, "KS-0101", null, 3m, "m³")), _world.Snapshot());

        Assert.Null(outcome.PurchaseOrder.PurchaseOrder);
        Assert.Equal(PurchaseOrderMatchMethod.None, outcome.PurchaseOrder.Method);
        Assert.True(outcome.PurchaseOrder.Candidates.Count > 1);
    }

    [Fact]
    public void A_missing_reference_where_no_order_has_enough_open_quantity_picks_none()
    {
        var outcome = MatchingEngine.Match(Invoice(TimberVat, null, Line(1, "KS-0101", null, 100m, "m³")), _world.Snapshot());

        Assert.Null(outcome.PurchaseOrder.PurchaseOrder);
        Assert.Empty(outcome.PurchaseOrder.Candidates);
    }

    [Fact]
    public void Quantity_already_invoiced_takes_an_order_out_of_the_running()
    {
        // PO-2026-0003 has 12 m³ of pine; 10 are already invoiced, so 5 m³ no longer fits.
        var snapshot = _world.Snapshot(invoiced: new Dictionary<int, decimal> { [31] = 10m });

        var outcome = MatchingEngine.Match(Invoice(TimberVat, null, Line(1, "KS-0103", null, 5m, "m³")), snapshot);

        Assert.Null(outcome.PurchaseOrder.PurchaseOrder);
    }

    [Fact]
    public void A_line_that_matches_no_item_means_no_order_is_suggested()
    {
        var outcome = MatchingEngine.Match(
            Invoice(TimberVat, null, Line(1, "KS-0103", null, 12m, "m³"), Line(2, "ZX-1234", "Sample set", 1m, "tk")), _world.Snapshot());

        Assert.Null(outcome.PurchaseOrder.PurchaseOrder);
    }

    [Fact]
    public void An_uncoded_charge_line_does_not_stop_the_search_for_the_order()
    {
        var outcome = MatchingEngine.Match(
            Invoice(TimberVat, null, Line(1, "KS-0103", null, 12m, "m³"), Line(2, null, "Fuel surcharge", 1m, "pcs")), _world.Snapshot());

        Assert.Same(_world.Po3, outcome.PurchaseOrder.PurchaseOrder);
    }

    [Fact]
    public void Orders_of_other_suppliers_are_never_candidates()
    {
        // Plywood is only on the panel supplier's order, but this invoice is from the timber supplier.
        var outcome = MatchingEngine.Match(Invoice(TimberVat, null, Line(1, "BBS00157", null, 10m, "pcs")), _world.Snapshot());

        Assert.Null(outcome.PurchaseOrder.PurchaseOrder);
    }
}
