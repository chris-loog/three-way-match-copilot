using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Erp;

namespace ThreeWayMatch.Domain.Matching;

/// <summary>
/// Everything the engine may look at, as plain objects. A loader in Infrastructure fills it from the database;
/// the engine itself never queries anything, so every rule can be tested with a hand-built snapshot.
/// </summary>
/// <remarks>
/// Entities must be loaded with their relations: <c>SupplierItem.Item</c>, <c>PurchaseOrder.Lines</c>,
/// <c>PurchaseOrderLine.SupplierItem</c> and <c>PurchaseOrderLine.ReceiptLines</c>.
/// </remarks>
public sealed class MatchingSnapshot
{
    public required IReadOnlyList<Supplier> Suppliers { get; init; }
    public required IReadOnlyList<SupplierItem> SupplierItems { get; init; }
    public IReadOnlyList<ItemAlias> Aliases { get; init; } = [];
    public required IReadOnlyList<PurchaseOrder> PurchaseOrders { get; init; }

    /// <summary>Quantity already invoiced per PO line id (approved, matched invoice lines), in the PO line's unit.</summary>
    public IReadOnlyDictionary<int, decimal> InvoicedQuantityByPurchaseOrderLine { get; init; } = new Dictionary<int, decimal>();

    public decimal InvoicedQuantity(PurchaseOrderLine line) =>
        InvoicedQuantityByPurchaseOrderLine.GetValueOrDefault(line.Id);

    /// <summary>What can still be invoiced on a PO line, in the PO line's unit. Never negative.</summary>
    public decimal OpenQuantity(PurchaseOrderLine line) => Math.Max(0, line.Quantity - InvoicedQuantity(line));
}
