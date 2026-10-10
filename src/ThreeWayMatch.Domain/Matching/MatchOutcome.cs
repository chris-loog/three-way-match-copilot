using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Erp;

namespace ThreeWayMatch.Domain.Matching;

public enum SupplierMatchMethod
{
    VatNumber,
    FuzzyName,
    None,
}

public enum PurchaseOrderMatchMethod
{
    /// <summary>The PO number printed on the document.</summary>
    Reference,

    /// <summary>No usable reference; exactly one open PO of the supplier fits the lines. Always needs a human look.</summary>
    SuggestedFromLines,
    None,
}

public enum PurchaseOrderReferenceStatus
{
    Matched,
    Missing,

    /// <summary>A reference was printed but no PO has that number.</summary>
    Unknown,

    /// <summary>The PO exists but belongs to another supplier.</summary>
    BelongsToOtherSupplier,

    /// <summary>The supplier could not be identified, so the reference cannot be checked against its orders.</summary>
    SupplierUnknown,
}

public enum UnitStatus
{
    /// <summary>The document uses the unit the supplier sells in.</summary>
    Same,

    /// <summary>The document uses our base unit (m³, pcs, l), so the quantity needs no factor.</summary>
    BaseUnit,

    /// <summary>The unit cannot be converted to the supplier's or our base unit, e.g. stacked against solid cubic metres.</summary>
    Mismatch,

    /// <summary>The unit is missing or not one we know.</summary>
    Unknown,
}

public enum ConcernKind
{
    FuzzySupplier,
    PurchaseOrderSuggested,
    FuzzyLineMatch,
    AmbiguousPurchaseOrderLine,
    AmbiguousChargeLine,
}

/// <summary>
/// Something that makes a human look at the document even if no discrepancy rule fires.
/// Fuzzy and suggested matches are guesses, and a guess must never approve a payment.
/// </summary>
public sealed record MatchConcern(ConcernKind Kind, int? LineNumber, string Detail);

public sealed record SupplierMatch(Supplier? Supplier, SupplierMatchMethod Method, double Confidence);

public sealed record PurchaseOrderMatch(
    PurchaseOrder? PurchaseOrder,
    PurchaseOrderMatchMethod Method,
    PurchaseOrderReferenceStatus ReferenceStatus,
    string? PrintedReference,
    IReadOnlyList<PurchaseOrder> Candidates);

/// <summary>The quantities on a PO line, all in the item's base unit, as they stood before this document.</summary>
public sealed record PurchaseOrderLineQuantities(decimal Ordered, decimal Received, decimal AlreadyInvoiced)
{
    /// <summary>What may still be invoiced against the order.</summary>
    public decimal OpenOnOrder => Math.Max(0, Ordered - AlreadyInvoiced);

    /// <summary>What may be invoiced against what has actually arrived. Invoicing more than this is over-invoicing.</summary>
    public decimal OpenOnReceipts => Math.Max(0, Received - AlreadyInvoiced);
}

/// <summary>
/// The result for one document line. Quantities (<see cref="ConvertedQuantity"/>, <see cref="GroupConvertedQuantity"/> and
/// <see cref="Quantities"/>) are in the item's base unit. Lines of the same item are summed in <see cref="GroupConvertedQuantity"/>.
/// </summary>
public sealed record LineMatchOutcome(
    MatchInputLine Line,
    SupplierItem? SupplierItem,
    MatchMethod Method,
    double Confidence,
    bool IsChargeLine,
    PurchaseOrderLine? PurchaseOrderLine,
    IReadOnlyList<GoodsReceiptLine> ReceiptLines,
    UnitStatus UnitStatus,
    decimal? ConvertedQuantity,
    decimal? GroupConvertedQuantity,
    PurchaseOrderLineQuantities? Quantities);

public sealed record MatchOutcome(
    SupplierMatch Supplier,
    PurchaseOrderMatch PurchaseOrder,
    IReadOnlyList<LineMatchOutcome> Lines,
    IReadOnlyList<MatchConcern> Concerns);
