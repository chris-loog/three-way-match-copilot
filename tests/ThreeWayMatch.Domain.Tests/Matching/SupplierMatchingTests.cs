using ThreeWayMatch.Domain.Matching;
using static ThreeWayMatch.Domain.Tests.Matching.MatchingWorld;

namespace ThreeWayMatch.Domain.Tests.Matching;

public class SupplierMatchingTests
{
    private readonly MatchingWorld _world = new();

    [Theory]
    [InlineData("EE102412824")]
    [InlineData("ee 102 412 824")]
    [InlineData("KMKR nr EE102412824")]
    [InlineData("102412824")]
    public void Finds_the_supplier_by_vat_number_however_it_was_printed(string printed)
    {
        var outcome = MatchingEngine.Match(Invoice(printed, null), _world.Snapshot());

        Assert.Same(_world.Timber, outcome.Supplier.Supplier);
        Assert.Equal(SupplierMatchMethod.VatNumber, outcome.Supplier.Method);
        Assert.DoesNotContain(outcome.Concerns, c => c.Kind == ConcernKind.FuzzySupplier);
    }

    [Fact]
    public void A_vat_number_beats_a_name_that_points_to_another_supplier()
    {
        var input = new MatchInput(TimberVat, "Baltic Board Supply OÜ", null, []);

        Assert.Same(_world.Timber, MatchingEngine.Match(input, _world.Snapshot()).Supplier.Supplier);
    }

    [Fact]
    public void Falls_back_to_a_close_name_and_says_it_was_a_guess()
    {
        var input = new MatchInput(null, "SILLAOTSA SAEVESKI", null, []);

        var outcome = MatchingEngine.Match(input, _world.Snapshot());

        Assert.Same(_world.Timber, outcome.Supplier.Supplier);
        Assert.Equal(SupplierMatchMethod.FuzzyName, outcome.Supplier.Method);
        Assert.Contains(outcome.Concerns, c => c.Kind == ConcernKind.FuzzySupplier);
    }

    [Fact]
    public void A_vat_number_nobody_has_falls_back_to_the_name()
    {
        var input = new MatchInput("EE100000999", "Sillaotsa Saeveski OÜ", null, []);

        Assert.Equal(SupplierMatchMethod.FuzzyName, MatchingEngine.Match(input, _world.Snapshot()).Supplier.Method);
    }

    [Fact]
    public void An_unknown_supplier_is_not_guessed_and_nothing_else_is_matched()
    {
        var input = new MatchInput("EE100000999", "Totally Different Ltd", "PO-2026-0001", [Line(1, "KS-0101", "Board", 5m, "m³")]);

        var outcome = MatchingEngine.Match(input, _world.Snapshot());

        Assert.Null(outcome.Supplier.Supplier);
        Assert.Equal(SupplierMatchMethod.None, outcome.Supplier.Method);
        Assert.Null(outcome.PurchaseOrder.PurchaseOrder);
        Assert.Equal(PurchaseOrderReferenceStatus.SupplierUnknown, outcome.PurchaseOrder.ReferenceStatus);
        Assert.Null(outcome.Lines[0].SupplierItem);
    }

    [Fact]
    public void Nothing_to_go_on_gives_no_supplier()
    {
        var outcome = MatchingEngine.Match(new MatchInput(null, null, null, []), _world.Snapshot());

        Assert.Equal(SupplierMatchMethod.None, outcome.Supplier.Method);
        Assert.Equal(PurchaseOrderReferenceStatus.Missing, outcome.PurchaseOrder.ReferenceStatus);
    }
}
