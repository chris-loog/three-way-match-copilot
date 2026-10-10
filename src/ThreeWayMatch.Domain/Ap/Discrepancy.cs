namespace ThreeWayMatch.Domain.Ap;

public enum DiscrepancyCode
{
    QtyOverInvoiced,
    PriceAbovePo,
    ItemNotOnPo,
    NoPoReference,
    DuplicateInvoice,
    TotalsMismatch,
    BankAccountChanged,
    DeliveryNoteMismatch,
    UomMismatch,
    PartialDelivery,
    PossibleDuplicate,
    PriceBelowPo,
    LowConfidence,
}

public enum Severity
{
    Info,
    Warning,
    Block,
}

/// <summary>One finding from a discrepancy rule.</summary>
public class Discrepancy
{
    public int Id { get; set; }
    public int MatchResultId { get; set; }
    public DiscrepancyCode Code { get; set; }
    public Severity Severity { get; set; }

    /// <summary>The line this finding is about; null for header-level findings.</summary>
    public int? LineMatchId { get; set; }

    public string? ExpectedValue { get; set; }
    public string? ActualValue { get; set; }
    public decimal AmountAtStake { get; set; }

    public MatchResult? MatchResult { get; set; }
    public LineMatch? LineMatch { get; set; }
}
