namespace ThreeWayMatch.Domain.Rules;

/// <summary>
/// The numbers the rules use. Defaults live here; a supplier can override the price and quantity tolerance
/// (timber volumes vary more than hardware, for instance).
/// </summary>
public sealed class DiscrepancyOptions
{
    /// <summary>A unit price may exceed the PO price by this percent...</summary>
    public decimal PriceTolerancePercent { get; init; } = 1m;

    /// <summary>...but never by more than this many euros per unit, however expensive the item.</summary>
    public decimal PriceToleranceCap { get; init; } = 5m;

    /// <summary>An invoiced quantity may exceed what has arrived and not yet been invoiced by this percent.</summary>
    public decimal QuantityTolerancePercent { get; init; } = 2m;

    /// <summary>Sums may be off by this many euros (rounding).</summary>
    public decimal TotalsTolerance { get; init; } = 0.05m;

    /// <summary>Estonian standard VAT, used when a line does not state its rate.</summary>
    public decimal StandardVatRate { get; init; } = 0.24m;

    /// <summary>Paying more than agreed above this amount of money is a Block, not a Warning.</summary>
    public decimal OverpaymentBlockThreshold { get; init; } = 500m;

    /// <summary>Same supplier and amount within this many days, under a different invoice number, is a possible duplicate.</summary>
    public int PossibleDuplicateWindowDays { get; init; } = 14;

    /// <summary>A key field read with less confidence than this is unreliable.</summary>
    public double ConfidenceThreshold { get; init; } = 0.80;
}
