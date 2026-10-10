using ThreeWayMatch.Domain.Erp;

namespace ThreeWayMatch.Domain.Ap;

public enum Decision
{
    AutoApproved,
    OnHold,
    NeedsReview,
    Rejected,
}

public enum MatchMethod
{
    SupplierItemCode,
    ItemCode,
    LearnedAlias,
    FuzzyDescription,
    None,
}

/// <summary>The outcome of matching one document. Temporal table: every change to the decision or override is kept in history.</summary>
public class MatchResult
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int? SupplierId { get; set; }
    public int? PurchaseOrderId { get; set; }
    public Decision Decision { get; set; }

    /// <summary>Total money at risk across all findings, used to sort the review queue.</summary>
    public decimal AmountAtStake { get; set; }

    public DateTimeOffset MatchedAt { get; set; }

    /// <summary>Set when a reviewer approves a document the engine did not approve. Mandatory then.</summary>
    public string? OverrideReason { get; set; }

    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }

    /// <summary>Ticked by the reviewer for BANK_ACCOUNT_CHANGED: "verified by phone with the supplier".</summary>
    public bool BankChangeVerified { get; set; }

    public Document? Document { get; set; }
    public Supplier? Supplier { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public List<LineMatch> LineMatches { get; } = [];
    public List<Discrepancy> Discrepancies { get; } = [];
}

/// <summary>Links one extracted line to the PO line and goods-receipt lines it was matched with, and records how.</summary>
public class LineMatch
{
    public int Id { get; set; }
    public int MatchResultId { get; set; }
    public int ExtractedLineId { get; set; }
    public int? PurchaseOrderLineId { get; set; }
    public MatchMethod Method { get; set; }
    public double Confidence { get; set; }

    /// <summary>Document quantity converted to the item's base unit; null when the unit cannot be converted.</summary>
    public decimal? ConvertedQuantity { get; set; }

    public MatchResult? MatchResult { get; set; }
    public ExtractedLine? ExtractedLine { get; set; }
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }
    public List<GoodsReceiptLine> GoodsReceiptLines { get; } = [];
}
