using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Matching;
using static ThreeWayMatch.Domain.Tests.Matching.MatchingWorld;

namespace ThreeWayMatch.Domain.Tests.Matching;

/// <summary>One test per cascade level: (a) supplier code, (b) our code, (c) learned alias, (d) fuzzy description.</summary>
public class ItemCascadeTests
{
    private readonly MatchingWorld _world = new();

    private LineMatchOutcome MatchLine(MatchInputLine line, MatchingSnapshot? snapshot = null) =>
        MatchingEngine.Match(Invoice(TimberVat, "PO-2026-0001", line), snapshot ?? _world.Snapshot()).Lines.Single();

    [Fact]
    public void Level_a_matches_the_suppliers_own_item_code_with_full_confidence()
    {
        var line = MatchLine(Line(1, "KS-0101", "anything at all", 5m, "m³"));

        Assert.Same(_world.Board, line.SupplierItem);
        Assert.Equal(MatchMethod.SupplierItemCode, line.Method);
        Assert.Equal(1.0, line.Confidence);
    }

    [Theory]
    [InlineData("ks 0101")]
    [InlineData("KS0101")]
    [InlineData(" ks-0101 ")]
    public void Level_a_ignores_case_spaces_and_punctuation_in_codes(string printed)
    {
        Assert.Equal(MatchMethod.SupplierItemCode, MatchLine(Line(1, printed, null, 5m, "m³")).Method);
    }

    [Fact]
    public void Level_b_matches_our_own_item_code()
    {
        var line = MatchLine(Line(1, "TMB-0001", null, 5m, "m³"));

        Assert.Same(_world.Board, line.SupplierItem);
        Assert.Equal(MatchMethod.ItemCode, line.Method);
        Assert.Equal(0.95, line.Confidence);
    }

    [Fact]
    public void Level_c_matches_a_description_a_reviewer_confirmed_before()
    {
        var alias = new ItemAlias { Id = 1, SupplierItemId = _world.Board.Id, SupplierItem = _world.Board, NormalisedDescription = "spruce plank 25 x 150", CreatedBy = "reviewer" };

        var line = MatchLine(Line(1, null, "  Spruce   PLANK 25 x 150 ", 5m, "m³"), _world.Snapshot(aliases: [alias]));

        Assert.Same(_world.Board, line.SupplierItem);
        Assert.Equal(MatchMethod.LearnedAlias, line.Method);
        Assert.Equal(0.90, line.Confidence);
    }

    [Fact]
    public void Level_c_ignores_an_alias_that_belongs_to_another_suppliers_item()
    {
        var alias = new ItemAlias { Id = 1, SupplierItemId = _world.Plywood.Id, SupplierItem = _world.Plywood, NormalisedDescription = "spruce plank 25 x 150", CreatedBy = "reviewer" };

        Assert.Null(MatchLine(Line(1, null, "Spruce plank 25 x 150", 5m, "m³"), _world.Snapshot(aliases: [alias])).SupplierItem);
    }

    [Fact]
    public void Level_d_matches_a_similar_description_but_only_as_a_guess_that_needs_review()
    {
        var outcome = MatchingEngine.Match(
            Invoice(TimberVat, "PO-2026-0001", Line(1, null, "Kuuselaud 25x150 mm kuivatatud 4,8m", 5m, "m³")), _world.Snapshot());

        var line = outcome.Lines.Single();
        Assert.Same(_world.Board, line.SupplierItem);
        Assert.Equal(MatchMethod.FuzzyDescription, line.Method);
        Assert.InRange(line.Confidence, 0.85, 1.0);
        Assert.Contains(outcome.Concerns, c => c.Kind == ConcernKind.FuzzyLineMatch && c.LineNumber == 1);
    }

    [Fact]
    public void Exact_levels_never_raise_a_fuzzy_concern()
    {
        var outcome = MatchingEngine.Match(Invoice(TimberVat, "PO-2026-0001", Line(1, "KS-0101", null, 5m, "m³")), _world.Snapshot());

        Assert.DoesNotContain(outcome.Concerns, c => c.Kind == ConcernKind.FuzzyLineMatch);
    }

    [Fact]
    public void A_description_that_resembles_nothing_matches_nothing()
    {
        var line = MatchLine(Line(1, null, "Garden gnome with fishing rod", 1m, "tk"));

        Assert.Null(line.SupplierItem);
        Assert.Equal(MatchMethod.None, line.Method);
        Assert.Null(line.PurchaseOrderLine);
    }

    [Fact]
    public void A_code_beats_a_description_that_points_elsewhere()
    {
        var line = MatchLine(Line(1, "KS-0101", "Männilaud 25x125 mm, hööveldatud", 5m, "m³"));

        Assert.Same(_world.Board, line.SupplierItem);
        Assert.Equal(MatchMethod.SupplierItemCode, line.Method);
    }

    [Fact]
    public void A_description_that_fits_two_items_equally_is_not_guessed()
    {
        // Two catalogue items with the same text: matching either one would be a coin flip, so match neither.
        var twin = new ThreeWayMatch.Domain.Erp.SupplierItem
        {
            Id = 90, Supplier = _world.Timber, SupplierId = 1, Item = _world.Board.Item, ItemId = 1,
            SupplierItemCode = "KS-0990", SupplierDescription = _world.Board.SupplierDescription, SupplierUnit = "m³", ConversionFactorToBase = 1m,
        };
        var snapshot = new MatchingSnapshot
        {
            Suppliers = [_world.Timber], SupplierItems = [_world.Board, twin], PurchaseOrders = [_world.Po1],
        };

        var line = MatchingEngine.Match(Invoice(TimberVat, "PO-2026-0001", Line(1, null, "Kuuselaud 25x150 mm, kuivatatud, 4,8 m", 5m, "m³")), snapshot).Lines.Single();

        Assert.Null(line.SupplierItem);
        Assert.Equal(MatchMethod.None, line.Method);
    }
}
