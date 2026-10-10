namespace ThreeWayMatch.Domain.Erp;

/// <summary>
/// How one supplier sells one of our items: their item code, their unit and the factor that converts
/// their unit to our base unit (1 pack = 0.25 m³ gives a factor of 0.25).
/// </summary>
public class SupplierItem
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public int ItemId { get; set; }
    public required string SupplierItemCode { get; set; }
    public required string SupplierDescription { get; set; }
    public required string SupplierUnit { get; set; }
    public decimal ConversionFactorToBase { get; set; }

    public Supplier? Supplier { get; set; }
    public Item? Item { get; set; }
}
