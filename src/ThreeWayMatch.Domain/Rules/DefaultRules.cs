using ThreeWayMatch.Domain.Rules.Rules;

namespace ThreeWayMatch.Domain.Rules;

public static class DefaultRules
{
    /// <summary>The whole discrepancy catalogue, one rule each (price has two: above and below).</summary>
    public static IReadOnlyList<IDiscrepancyRule> Create() =>
    [
        new QtyOverInvoicedRule(),
        new PartialDeliveryRule(),
        new PriceAbovePoRule(),
        new PriceBelowPoRule(),
        new ItemNotOnPoRule(),
        new NoPoReferenceRule(),
        new DuplicateInvoiceRule(),
        new PossibleDuplicateRule(),
        new TotalsMismatchRule(),
        new BankAccountChangedRule(),
        new UomMismatchRule(),
        new DeliveryNoteMismatchRule(),
        new LowConfidenceRule(),
    ];
}
