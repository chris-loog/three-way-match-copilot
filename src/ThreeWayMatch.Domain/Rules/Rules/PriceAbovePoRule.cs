using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Matching;

namespace ThreeWayMatch.Domain.Rules.Rules;

/// <summary>PRICE_ABOVE_PO: unit price above the PO price by more than the tolerance. Escalates to Block above the euro threshold.</summary>
public sealed class PriceAbovePoRule : IDiscrepancyRule
{
    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        if (context.Kind != DocumentType.Invoice)
        {
            yield break;
        }

        foreach (var line in context.Match.Lines)
        {
            if (PriceComparison.TryCompare(line, context, out var poPrice, out var invoicePrice, out var tolerance)
                && invoicePrice > poPrice + tolerance)
            {
                var amount = Math.Round((invoicePrice - poPrice) * (line.Line.Quantity ?? 0), 2);
                yield return new Finding(DiscrepancyCode.PriceAbovePo, Tolerances.Overpayment(amount, context.Options),
                    line.Line.LineNumber, Tolerances.Text(poPrice), Tolerances.Text(invoicePrice), amount);
            }
        }
    }
}

/// <summary>PRICE_BELOW_PO: paying less than agreed is no risk, so this is Info only, but the reviewer still sees it.</summary>
public sealed class PriceBelowPoRule : IDiscrepancyRule
{
    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        if (context.Kind != DocumentType.Invoice)
        {
            yield break;
        }

        foreach (var line in context.Match.Lines)
        {
            if (PriceComparison.TryCompare(line, context, out var poPrice, out var invoicePrice, out var tolerance)
                && invoicePrice < poPrice - tolerance)
            {
                yield return new Finding(DiscrepancyCode.PriceBelowPo, Severity.Info,
                    line.Line.LineNumber, Tolerances.Text(poPrice), Tolerances.Text(invoicePrice), 0m);
            }
        }
    }
}

internal static class PriceComparison
{
    /// <summary>
    /// Puts the PO price and the invoice price in the same unit, if that is possible. A price per m³ on the invoice
    /// against a price per pack on the order is divided by the pack factor first. If the unit cannot be converted,
    /// there is nothing to compare and the unit rule reports it.
    /// </summary>
    public static bool TryCompare(LineMatchOutcome line, RuleContext context, out decimal poPrice, out decimal invoicePrice, out decimal tolerance)
    {
        poPrice = invoicePrice = tolerance = 0;
        if (line.PurchaseOrderLine is not { } poLine || line.Line.UnitPrice is not { } price || line.UnitStatus is UnitStatus.Mismatch or UnitStatus.Unknown)
        {
            return false;
        }

        var factor = line.SupplierItem?.ConversionFactorToBase is > 0 ? line.SupplierItem.ConversionFactorToBase : 1m;
        poPrice = line.UnitStatus == UnitStatus.BaseUnit ? poLine.UnitPrice / factor : poLine.UnitPrice;
        invoicePrice = price;
        tolerance = Tolerances.Price(poPrice, context.Supplier, context.Options);
        return true;
    }
}
