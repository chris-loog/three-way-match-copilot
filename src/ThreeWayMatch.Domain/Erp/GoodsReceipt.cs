namespace ThreeWayMatch.Domain.Erp;

/// <summary>One delivery booked by the warehouse. A PO line can be delivered in several receipts.</summary>
public class GoodsReceipt
{
    public int Id { get; set; }
    public required string Number { get; set; }
    public int PurchaseOrderId { get; set; }
    public DateOnly ReceivedDate { get; set; }

    /// <summary>The number printed on the supplier's delivery note, as written down at the dock.</summary>
    public string? DeliveryNoteNumber { get; set; }

    public PurchaseOrder? PurchaseOrder { get; set; }
    public List<GoodsReceiptLine> Lines { get; } = [];
}

public class GoodsReceiptLine
{
    public int Id { get; set; }
    public int GoodsReceiptId { get; set; }
    public int PurchaseOrderLineId { get; set; }

    /// <summary>Received quantity in the PO line's unit.</summary>
    public decimal Quantity { get; set; }

    public GoodsReceipt? GoodsReceipt { get; set; }
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }
}
