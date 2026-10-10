using ThreeWayMatch.Domain.Erp;
using ThreeWayMatch.Domain.Normalisation;

namespace ThreeWayMatch.Domain.Matching;

/// <summary>Turns a document quantity into the item's base unit, or says why it cannot.</summary>
internal static class UnitConversion
{
    public static (UnitStatus Status, decimal? BaseQuantity) ToBase(string? documentUnit, decimal? quantity, SupplierItem item)
    {
        if (!Normaliser.TryNormaliseUnit(documentUnit, out var document))
        {
            return (UnitStatus.Unknown, null);
        }

        var supplierUnitKnown = Normaliser.TryNormaliseUnit(item.SupplierUnit, out var supplierUnit);
        var baseUnitKnown = Normaliser.TryNormaliseUnit(item.Item?.BaseUnit, out var baseUnit);

        // The supplier's own unit: apply the factor (1 pack = 0.25 m³).
        if (supplierUnitKnown && document == supplierUnit)
        {
            return (UnitStatus.Same, quantity * item.ConversionFactorToBase);
        }

        // Already in our base unit: nothing to convert.
        if (baseUnitKnown && document == baseUnit)
        {
            return (UnitStatus.BaseUnit, quantity);
        }

        // Anything else is not convertible. In particular solid and stacked cubic metres are never converted into each other.
        return (UnitStatus.Mismatch, null);
    }
}
