using ThreeWayMatch.Domain.Erp;

namespace DocGen.Seeding;

/// <summary>The complete mock-ERP data set, as an object graph that has not been saved anywhere yet.</summary>
internal sealed record ErpData(
    IReadOnlyList<Supplier> Suppliers,
    IReadOnlyList<Item> Items,
    IReadOnlyList<SupplierItem> SupplierItems,
    IReadOnlyList<PurchaseOrder> PurchaseOrders,
    IReadOnlyList<GoodsReceipt> GoodsReceipts);
