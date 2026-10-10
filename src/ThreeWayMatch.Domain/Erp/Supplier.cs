namespace ThreeWayMatch.Domain.Erp;

/// <summary>A supplier in the mock ERP. Tolerance overrides are optional; null means "use the configured default".</summary>
public class Supplier
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
    public required string VatNumber { get; set; }
    public required string RegistryCode { get; set; }

    /// <summary>The bank account on the master data, normalised (no spaces, upper case). Compared with the IBAN on invoices.</summary>
    public required string Iban { get; set; }

    /// <summary>Decides ambiguous numbers and dates on this supplier's documents, e.g. <c>et-EE</c> or <c>en-GB</c>.</summary>
    public required string DocumentCulture { get; set; }

    public decimal? PriceTolerancePercent { get; set; }
    public decimal? QuantityTolerancePercent { get; set; }
}
