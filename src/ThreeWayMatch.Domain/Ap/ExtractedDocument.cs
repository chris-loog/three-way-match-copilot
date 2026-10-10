namespace ThreeWayMatch.Domain.Ap;

/// <summary>What Document Intelligence read from a document: typed header fields, the lines, and the raw JSON.</summary>
public class ExtractedDocument
{
    public int Id { get; set; }
    public int DocumentId { get; set; }

    /// <summary>The Document Intelligence model that produced this result, e.g. <c>prebuilt-invoice</c>.</summary>
    public required string ModelId { get; set; }

    public string? SupplierName { get; set; }
    public string? SupplierVatNumber { get; set; }

    /// <summary>Invoice number for invoices, delivery note number for delivery notes.</summary>
    public string? DocumentNumber { get; set; }

    public string? PurchaseOrderReference { get; set; }
    public DateOnly? DocumentDate { get; set; }
    public decimal? NetTotal { get; set; }
    public decimal? VatTotal { get; set; }
    public decimal? GrossTotal { get; set; }
    public string? Currency { get; set; }
    public string? Iban { get; set; }

    /// <summary>The lowest confidence among the key fields. Compared with the review threshold.</summary>
    public double? MinKeyFieldConfidence { get; set; }

    /// <summary>Confidence per field as JSON, e.g. <c>{"InvoiceId":0.97,"Iban":0.62}</c>.</summary>
    public string? FieldConfidenceJson { get; set; }

    /// <summary>The unmodified Document Intelligence response.</summary>
    public required string RawJson { get; set; }

    public DateTimeOffset ExtractedAt { get; set; }

    public Document? Document { get; set; }
    public List<ExtractedLine> Lines { get; } = [];
}

public class ExtractedLine
{
    public int Id { get; set; }
    public int ExtractedDocumentId { get; set; }
    public int LineNumber { get; set; }
    public string? SupplierItemCode { get; set; }
    public string? Description { get; set; }
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? VatRate { get; set; }
    public decimal? LineTotal { get; set; }
    public double? Confidence { get; set; }

    public ExtractedDocument? ExtractedDocument { get; set; }
}
