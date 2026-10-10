using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Matching;
using static ThreeWayMatch.Domain.Tests.Matching.MatchingWorld;

namespace ThreeWayMatch.Domain.Tests.Matching;

public class LineAllocationTests
{
    private readonly MatchingWorld _world = new();

    private MatchOutcome Match(string poNumber, MatchingSnapshot? snapshot = null, params MatchInputLine[] lines) =>
        MatchingEngine.Match(Invoice(TimberVat, poNumber, lines), snapshot ?? _world.Snapshot());

    [Fact]
    public void A_line_is_matched_to_its_order_line_with_the_goods_receipts_and_base_unit_quantities()
    {
        var line = Match("PO-2026-0001", null, Line(1, "KS-0101", null, 10m, "m³")).Lines.Single();

        Assert.Same(_world.Po1.Lines[0], line.PurchaseOrderLine);
        Assert.Single(line.ReceiptLines);
        Assert.Equal(UnitStatus.Same, line.UnitStatus);
        Assert.Equal(10m, line.ConvertedQuantity);
        Assert.Equal(new PurchaseOrderLineQuantities(Ordered: 10m, Received: 10m, AlreadyInvoiced: 0m), line.Quantities);
    }

    [Fact]
    public void Quantity_already_invoiced_is_taken_off_what_may_still_be_invoiced()
    {
        var snapshot = _world.Snapshot(invoiced: new Dictionary<int, decimal> { [11] = 4m });

        var quantities = Match("PO-2026-0001", snapshot, Line(1, "KS-0101", null, 6m, "m³")).Lines.Single().Quantities!;

        Assert.Equal(4m, quantities.AlreadyInvoiced);
        Assert.Equal(6m, quantities.OpenOnReceipts);
        Assert.Equal(6m, quantities.OpenOnOrder);
    }

    [Fact]
    public void Open_quantity_on_receipts_is_what_has_arrived_minus_what_was_invoiced()
    {
        // Lath: 20 packs ordered (5 m³), 8 packs received (2 m³), none invoiced yet.
        var quantities = Match("PO-2026-0001", null, Line(1, "KS-0102", null, 8m, "pakk")).Lines.Single().Quantities!;

        Assert.Equal(5m, quantities.Ordered);
        Assert.Equal(2m, quantities.Received);
        Assert.Equal(2m, quantities.OpenOnReceipts);
        Assert.Equal(5m, quantities.OpenOnOrder);
    }

    [Fact]
    public void Packs_are_converted_to_the_base_unit_with_the_suppliers_factor()
    {
        var line = Match("PO-2026-0001", null, Line(1, "KS-0102", null, 8m, "pakk")).Lines.Single();

        Assert.Equal(UnitStatus.Same, line.UnitStatus);
        Assert.Equal(2m, line.ConvertedQuantity); // 8 packs x 0.25 m³
    }

    [Fact]
    public void A_quantity_already_in_the_base_unit_needs_no_conversion()
    {
        var line = Match("PO-2026-0001", null, Line(1, "KS-0102", null, 2m, "m³")).Lines.Single();

        Assert.Equal(UnitStatus.BaseUnit, line.UnitStatus);
        Assert.Equal(2m, line.ConvertedQuantity);
    }

    [Fact]
    public void Stacked_cubic_metres_are_never_converted_to_solid_ones()
    {
        var line = Match("PO-2026-0001", null, Line(1, "KS-0101", null, 10m, "rm")).Lines.Single();

        Assert.Equal(UnitStatus.Mismatch, line.UnitStatus);
        Assert.Null(line.ConvertedQuantity);
        Assert.Same(_world.Po1.Lines[0], line.PurchaseOrderLine); // still matched, so the rule can report it
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("furlongs")]
    public void A_missing_or_unknown_unit_is_reported_not_assumed(string? unit)
    {
        var line = Match("PO-2026-0001", null, Line(1, "KS-0101", null, 10m, unit)).Lines.Single();

        Assert.Equal(UnitStatus.Unknown, line.UnitStatus);
        Assert.Null(line.ConvertedQuantity);
    }

    [Fact]
    public void Lines_for_the_same_item_are_summed_and_share_one_order_line()
    {
        var outcome = Match("PO-2026-0001", null, Line(1, "KS-0101", null, 4m, "m³"), Line(2, "KS-0101", null, 3m, "m³"));

        Assert.All(outcome.Lines, l => Assert.Same(_world.Po1.Lines[0], l.PurchaseOrderLine));
        Assert.All(outcome.Lines, l => Assert.Equal(7m, l.GroupConvertedQuantity));
        Assert.Equal([4m, 3m], outcome.Lines.Select(l => l.ConvertedQuantity!.Value));
    }

    [Theory]
    [InlineData(5, 51)] // 5 m³ fits the 6 m³ line best
    [InlineData(8, 52)] // 8 m³ does not fit 6, so the 10 m³ line
    [InlineData(10, 52)]
    public void When_an_item_is_on_two_order_lines_the_line_that_just_fits_is_taken(int quantity, int expectedLineId)
    {
        var line = Match("PO-2026-0005", null, Line(1, "KS-0101", null, quantity, "m³")).Lines.Single();

        Assert.Equal(expectedLineId, line.PurchaseOrderLine!.Id);
    }

    [Fact]
    public void When_the_quantity_fits_neither_line_the_larger_open_quantity_is_taken()
    {
        var line = Match("PO-2026-0005", null, Line(1, "KS-0101", null, 15m, "m³")).Lines.Single();

        Assert.Equal(52, line.PurchaseOrderLine!.Id);
    }

    [Fact]
    public void Two_order_lines_that_fit_equally_well_are_a_tie_for_a_human_to_settle()
    {
        var outcome = Match("PO-2026-0004", null, Line(1, "KS-0101", null, 4m, "m³"));

        Assert.Contains(outcome.Concerns, c => c.Kind == ConcernKind.AmbiguousPurchaseOrderLine);
        Assert.NotNull(outcome.Lines[0].PurchaseOrderLine);
    }

    [Fact]
    public void Open_quantity_breaks_what_looks_like_a_tie()
    {
        // Same two 10 m³ lines, but 8 m³ are already invoiced on the first: the second is the better fit.
        var snapshot = _world.Snapshot(invoiced: new Dictionary<int, decimal> { [41] = 8m });

        var outcome = Match("PO-2026-0004", snapshot, Line(1, "KS-0101", null, 9m, "m³"));

        Assert.Equal(42, outcome.Lines[0].PurchaseOrderLine!.Id);
        Assert.Empty(outcome.Concerns);
    }

    [Fact]
    public void A_charge_line_without_a_code_is_matched_to_the_orders_charge_line_by_keyword()
    {
        var line = Match("PO-2026-0001", null, Line(1, null, "Transport", 1m, "reis")).Lines.Single();

        Assert.True(line.IsChargeLine);
        Assert.Equal(MatchMethod.ChargeKeyword, line.Method);
        Assert.Same(_world.Po1.Lines[2], line.PurchaseOrderLine);
        Assert.Same(_world.Freight, line.SupplierItem);
    }

    [Theory]
    [InlineData("Fuel surcharge")]
    [InlineData("Kütuselisa")]
    [InlineData("Allahindlus")]
    public void A_charge_the_order_has_no_line_for_is_reported_as_not_on_the_order(string description)
    {
        // PO-2026-0002 has no charge line.
        var line = Match("PO-2026-0002", null, Line(1, null, description, 1m, "tk")).Lines.Single();

        Assert.True(line.IsChargeLine);
        Assert.Null(line.PurchaseOrderLine);
        Assert.Null(line.SupplierItem);
        Assert.Equal(MatchMethod.None, line.Method);
    }

    [Fact]
    public void An_item_the_supplier_sells_but_the_order_does_not_contain_has_no_order_line()
    {
        // Lath exists in the catalogue but PO-2026-0002 only orders board.
        var line = Match("PO-2026-0002", null, Line(1, "KS-0102", null, 4m, "pakk")).Lines.Single();

        Assert.Same(_world.Lath, line.SupplierItem);
        Assert.Null(line.PurchaseOrderLine);
        Assert.Null(line.Quantities);
    }

    [Fact]
    public void Without_a_purchase_order_lines_are_still_resolved_to_items()
    {
        var outcome = Match("PO-2026-9999", null, Line(1, "KS-0101", null, 100m, "m³"));

        Assert.Null(outcome.PurchaseOrder.PurchaseOrder);
        Assert.Same(_world.Board, outcome.Lines[0].SupplierItem);
        Assert.Null(outcome.Lines[0].PurchaseOrderLine);
        Assert.Equal(100m, outcome.Lines[0].ConvertedQuantity);
    }

    [Fact]
    public void Results_come_back_in_the_order_of_the_document_lines()
    {
        var outcome = Match("PO-2026-0001", null,
            Line(3, "KS-0102", null, 1m, "pakk"), Line(1, "KS-0101", null, 1m, "m³"), Line(2, null, "Transport", 1m, "reis"));

        Assert.Equal([3, 1, 2], outcome.Lines.Select(l => l.Line.LineNumber));
    }

    [Fact]
    public void A_delivery_note_line_without_a_price_goes_through_the_same_engine()
    {
        var note = new MatchInput(TimberVat, null, "PO-2026-0001", [new MatchInputLine(1, "KS-0101", "Kuuselaud", 10m, "m³", null)]);

        var line = MatchingEngine.Match(note, _world.Snapshot()).Lines.Single();

        Assert.Same(_world.Po1.Lines[0], line.PurchaseOrderLine);
    }

    [Fact]
    public void A_line_without_a_quantity_is_still_matched_and_has_no_converted_quantity()
    {
        var line = Match("PO-2026-0001", null, Line(1, "KS-0101", null, null, "m³")).Lines.Single();

        Assert.Same(_world.Board, line.SupplierItem);
        Assert.Null(line.ConvertedQuantity);
    }
}
