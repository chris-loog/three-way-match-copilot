using FuzzySharp;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Erp;
using ThreeWayMatch.Domain.Normalisation;

namespace ThreeWayMatch.Domain.Matching;

internal readonly record struct ItemResolution(SupplierItem? Item, MatchMethod Method, double Confidence)
{
    public static ItemResolution None { get; } = new(null, MatchMethod.None, 0);
}

/// <summary>
/// Works out which of the supplier's items a document line is about. Three exact levels, then a guess:
/// (a) the supplier's own item code, (b) our item code, (c) an alias a reviewer confirmed earlier, (d) a similar description.
/// </summary>
internal sealed class ItemResolver(IReadOnlyList<SupplierItem> supplierItems, IReadOnlyList<ItemAlias> aliases)
{
    private const int MinimumFuzzyScore = 85;
    private const int MinimumLeadOverRunnerUp = 3;

    public ItemResolution ResolveExact(MatchInputLine line)
    {
        var codeKey = Normaliser.ReferenceKey(line.ItemCode);
        if (codeKey.Length > 0)
        {
            var bySupplierCode = supplierItems.FirstOrDefault(i => Normaliser.ReferenceKey(i.SupplierItemCode) == codeKey);
            if (bySupplierCode is not null)
            {
                return new ItemResolution(bySupplierCode, MatchMethod.SupplierItemCode, 1.0);
            }

            var byOurCode = supplierItems.FirstOrDefault(i => i.Item is not null && Normaliser.ReferenceKey(i.Item.Code) == codeKey);
            if (byOurCode is not null)
            {
                return new ItemResolution(byOurCode, MatchMethod.ItemCode, 0.95);
            }
        }

        var description = Normaliser.NormaliseDescription(line.Description);
        if (description.Length > 0)
        {
            var alias = aliases.FirstOrDefault(a => a.NormalisedDescription == description && supplierItems.Any(i => IsSame(i, a)));
            if (alias is not null)
            {
                return new ItemResolution(supplierItems.First(i => IsSame(i, alias)), MatchMethod.LearnedAlias, 0.90);
            }
        }

        return ItemResolution.None;
    }

    /// <summary>
    /// A guess from the description. Needs a clear winner: a close runner-up means the description fits two items,
    /// and then it is better to match nothing than the wrong one.
    /// </summary>
    public ItemResolution ResolveFuzzy(MatchInputLine line)
    {
        var description = Normaliser.NormaliseDescription(line.Description);
        if (description.Length == 0 || supplierItems.Count == 0)
        {
            return ItemResolution.None;
        }

        var scored = supplierItems
            .Select(i => (Item: i, Score: Math.Max(
                Fuzz.WeightedRatio(description, Normaliser.NormaliseDescription(i.SupplierDescription)),
                Fuzz.WeightedRatio(description, Normaliser.NormaliseDescription(i.Item?.Name)))))
            .OrderByDescending(x => x.Score)
            .ToList();

        var best = scored[0];
        var runnerUp = scored.Count > 1 ? scored[1].Score : 0;
        return best.Score >= MinimumFuzzyScore && best.Score - runnerUp >= MinimumLeadOverRunnerUp
            ? new ItemResolution(best.Item, MatchMethod.FuzzyDescription, best.Score / 100.0)
            : ItemResolution.None;
    }

    private static bool IsSame(SupplierItem item, ItemAlias alias) =>
        alias.SupplierItemId != 0 ? item.Id == alias.SupplierItemId : ReferenceEquals(item, alias.SupplierItem);
}
