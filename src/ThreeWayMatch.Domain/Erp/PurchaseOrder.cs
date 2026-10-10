namespace ThreeWayMatch.Domain.Erp;

public class PurchaseOrder
{
    public int Id { get; set; }
    public required string Number { get; set; }
    public int SupplierId { get; set; }
    public DateOnly OrderDate { get; set; }
    public required string Currency { get; set; }

    public Supplier? Supplier { get; set; }
    public List<PurchaseOrderLine> Lines { get; } = [];
}

public enum PurchaseOrderLineStatus
{
    Open,
    PartiallyReceived,
    Received,

    /// <summary>Closed by hand, e.g. when a short delivery is accepted.</summary>
    Closed,
}

/// <summary>A PO line, stored in the supplier's unit exactly as it appears on the supplier's documents.</summary>
public class PurchaseOrderLine
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public int LineNumber { get; set; }
    public int SupplierItemId { get; set; }
    public required string Description { get; set; }
    public decimal Quantity { get; set; }
    public required string Unit { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; }
    public PurchaseOrderLineStatus Status { get; set; }

    /// <summary>Optimistic concurrency token: two invoices updating the same line at once cannot both win.</summary>
    public byte[] RowVersion { get; set; } = [];

    public PurchaseOrder? PurchaseOrder { get; set; }
    public SupplierItem? SupplierItem { get; set; }
    public List<GoodsReceiptLine> ReceiptLines { get; } = [];
}
