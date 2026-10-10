using ThreeWayMatch.Domain.Erp;

namespace ThreeWayMatch.Domain.Matching;

/// <summary>
/// Who owns what. Entities loaded from the database are linked by id; objects built by hand (in tests) are linked by
/// reference. Both count.
/// </summary>
internal static class Ownership
{
    public static bool Owns(Supplier supplier, SupplierItem item) =>
        ReferenceEquals(item.Supplier, supplier) || (supplier.Id != 0 && item.SupplierId == supplier.Id);

    public static bool Owns(Supplier supplier, PurchaseOrder order) =>
        ReferenceEquals(order.Supplier, supplier) || (supplier.Id != 0 && order.SupplierId == supplier.Id);

    public static bool IsLineOf(PurchaseOrderLine line, SupplierItem item) =>
        ReferenceEquals(line.SupplierItem, item) || (item.Id != 0 && line.SupplierItemId == item.Id);
}
