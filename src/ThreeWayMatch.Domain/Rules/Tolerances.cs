using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Erp;

namespace ThreeWayMatch.Domain.Rules;

internal static class Tolerances
{
    /// <summary>The most a unit price may differ from the PO price: a percentage, capped in euros. A supplier may override the percentage.</summary>
    public static decimal Price(decimal poPrice, Supplier? supplier, DiscrepancyOptions options)
    {
        var percent = supplier?.PriceTolerancePercent ?? options.PriceTolerancePercent;
        return Math.Min(Math.Abs(poPrice) * percent / 100m, options.PriceToleranceCap);
    }

    public static decimal Quantity(decimal quantity, Supplier? supplier, DiscrepancyOptions options)
    {
        var percent = supplier?.QuantityTolerancePercent ?? options.QuantityTolerancePercent;
        return Math.Abs(quantity) * percent / 100m;
    }

    /// <summary>
    /// Overpayment escalation: a small overcharge is a Warning, but above the threshold it is a Block,
    /// because paying more than agreed is a direct money risk.
    /// </summary>
    public static Severity Overpayment(decimal amount, DiscrepancyOptions options) =>
        amount > options.OverpaymentBlockThreshold ? Severity.Block : Severity.Warning;

    public static string Text(decimal value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}
