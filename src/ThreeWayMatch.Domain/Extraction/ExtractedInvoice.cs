using ThreeWayMatch.Domain.Normalisation;

namespace ThreeWayMatch.Domain.Extraction;

/// <summary>An invoice as read from a document. Nothing here is trusted yet: it is checked and matched later.</summary>
public sealed record ExtractedInvoice
{
    /// <summary>The Document Intelligence model that produced this result, e.g. <c>prebuilt-invoice</c>.</summary>
    public required string ModelId { get; init; }

    /// <summary>The unmodified Document Intelligence result. Kept so mapping can be re-run without calling Azure again.</summary>
    public required string RawJson { get; init; }

    public ExtractedField<string> SupplierName { get; init; } = ExtractedField.Missing<string>();
    public ExtractedField<string> SupplierVatNumber { get; init; } = ExtractedField.Missing<string>();
    public ExtractedField<string> SupplierRegistryCode { get; init; } = ExtractedField.Missing<string>();
    public ExtractedField<string> InvoiceNumber { get; init; } = ExtractedField.Missing<string>();
    public ExtractedField<string> PurchaseOrderReference { get; init; } = ExtractedField.Missing<string>();
    public ExtractedField<DateOnly?> InvoiceDate { get; init; } = ExtractedField.Missing<DateOnly?>();
    public ExtractedField<decimal?> NetTotal { get; init; } = ExtractedField.Missing<decimal?>();
    public ExtractedField<decimal?> VatTotal { get; init; } = ExtractedField.Missing<decimal?>();
    public ExtractedField<decimal?> GrossTotal { get; init; } = ExtractedField.Missing<decimal?>();
    public ExtractedField<string> Currency { get; init; } = ExtractedField.Missing<string>();
    public ExtractedField<string> Iban { get; init; } = ExtractedField.Missing<string>();

    public IReadOnlyList<ExtractedInvoiceLine> Lines { get; init; } = [];

    /// <summary>
    /// The fields a wrong value in would cost money or hide fraud. Every one of them must be sure enough
    /// before a document can be approved automatically. Fields that were not found at all count as unreadable (confidence 0),
    /// except the PO reference: an invoice may simply not carry one, and NO_PO_REFERENCE reports that on its own.
    /// A PO reference that is there but read with doubt still counts.
    /// </summary>
    public IReadOnlyDictionary<string, double> KeyFieldConfidences()
    {
        var confidences = new Dictionary<string, double>
        {
            ["SupplierVatNumber"] = SupplierVatNumber.Confidence ?? 0,
            ["InvoiceNumber"] = InvoiceNumber.Confidence ?? 0,
            ["NetTotal"] = NetTotal.Confidence ?? 0,
            ["VatTotal"] = VatTotal.Confidence ?? 0,
            ["GrossTotal"] = GrossTotal.Confidence ?? 0,
            ["Iban"] = Iban.Confidence ?? 0,
        };

        if (PurchaseOrderReference.HasValue)
        {
            confidences["PurchaseOrderReference"] = PurchaseOrderReference.Confidence ?? 0;
        }

        foreach (var line in Lines)
        {
            confidences[$"Line{line.LineNumber}.Quantity"] = line.Quantity.Confidence ?? 0;
            confidences[$"Line{line.LineNumber}.UnitPrice"] = line.UnitPrice.Confidence ?? 0;
        }

        return confidences;
    }

    public double MinKeyFieldConfidence() => KeyFieldConfidences().Values.Min();

    /// <summary>
    /// The key fields below the threshold, worst first. Empty means the extraction is sure enough.
    /// When <paramref name="reconcileTolerance"/> is given and the invoice adds up on its own (see
    /// <see cref="InvoiceReconciliation"/>), the gross total and the line quantities and prices are not reported: the arithmetic
    /// backs them. The stored confidences stay as the model gave them.
    /// </summary>
    public IReadOnlyList<string> LowConfidenceFields(double threshold, decimal? reconcileTolerance = null)
    {
        var reconciles = reconcileTolerance is { } tolerance && InvoiceReconciliation.Reconciles(this, tolerance);

        return KeyFieldConfidences()
            .Where(f => f.Value < threshold && !(reconciles && IsBackedByArithmetic(f.Key)))
            .OrderBy(f => f.Value)
            .Select(f => f.Key)
            .ToList();
    }

    private static bool IsBackedByArithmetic(string field) =>
        field == "GrossTotal" || (field.StartsWith("Line", StringComparison.Ordinal) && (field.EndsWith(".Quantity", StringComparison.Ordinal) || field.EndsWith(".UnitPrice", StringComparison.Ordinal)));
}

public sealed record ExtractedInvoiceLine(
    int LineNumber,
    ExtractedField<string> ItemCode,
    ExtractedField<string> Description,
    ExtractedField<decimal?> Quantity,
    ExtractedField<string> Unit,
    ExtractedField<decimal?> UnitPrice,
    ExtractedField<decimal?> VatRate,
    ExtractedField<decimal?> LineTotal);
