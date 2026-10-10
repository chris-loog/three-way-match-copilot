using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Erp;
using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Domain.Matching;

namespace ThreeWayMatch.Domain.Rules;

/// <summary>One thing a rule found wrong. It carries what it expected, what it saw, and how much money is at risk.</summary>
public sealed record Finding(
    DiscrepancyCode Code,
    Severity Severity,
    int? LineNumber,
    string? Expected,
    string? Actual,
    decimal AmountAtStake);

/// <summary>An invoice that was processed earlier. The duplicate rules compare new invoices with these.</summary>
public sealed record ProcessedInvoice(
    string? ContentHash,
    int? SupplierId,
    string? InvoiceNumber,
    decimal? GrossTotal,
    DateOnly? InvoiceDate,
    int? PurchaseOrderId = null);

/// <summary>Everything a rule may look at. Rules read it; none of them changes it.</summary>
public sealed class RuleContext
{
    public required DocumentType Kind { get; init; }
    public required MatchOutcome Match { get; init; }
    public required MatchingSnapshot Snapshot { get; init; }
    public DiscrepancyOptions Options { get; init; } = new();

    /// <summary>The extracted invoice, already cross-checked. Null for delivery notes.</summary>
    public ExtractedInvoice? Invoice { get; init; }

    /// <summary>SHA-256 of the file, for the exact-duplicate check.</summary>
    public string? ContentHash { get; init; }

    public IReadOnlyList<ProcessedInvoice> ProcessedInvoices { get; init; } = [];

    /// <summary>The number printed on a delivery note, to find the goods receipt the warehouse booked for it.</summary>
    public string? DeliveryNoteNumber { get; init; }

    /// <summary>Key fields the extraction was not sure about (for a delivery note, supplied by the caller).</summary>
    public IReadOnlyList<string> LowConfidenceFields { get; init; } = [];

    public Supplier? Supplier => Match.Supplier.Supplier;
}
