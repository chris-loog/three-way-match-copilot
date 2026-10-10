namespace ThreeWayMatch.Domain.Erp;

/// <summary>An item in our own catalogue, counted in its base (stock) unit, e.g. m³ for timber or pcs for hardware.</summary>
public class Item
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string BaseUnit { get; set; }
}
