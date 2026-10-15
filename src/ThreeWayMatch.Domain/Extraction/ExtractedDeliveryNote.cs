namespace ThreeWayMatch.Domain.Extraction;

/// <summary>A supplier's delivery note as read from a document. Like <see cref="ExtractedInvoice"/>, nothing here is trusted yet.</summary>
public sealed record ExtractedDeliveryNote
{
    /// <summary>The model that produced this result: the custom model's id, or <c>prebuilt-layout</c> for the rules fallback.</summary>
    public required string ModelId { get; init; }

    /// <summary>The unmodified Document Intelligence result, kept so mapping can be re-run without calling Azure.</summary>
    public required string RawJson { get; init; }

    public ExtractedField<string> SupplierName { get; init; } = ExtractedField.Missing<string>();
    public ExtractedField<string> SupplierVatNumber { get; init; } = ExtractedField.Missing<string>();
    public ExtractedField<string> DeliveryNoteNumber { get; init; } = ExtractedField.Missing<string>();
    public ExtractedField<string> PurchaseOrderReference { get; init; } = ExtractedField.Missing<string>();
    public ExtractedField<DateOnly?> DeliveryDate { get; init; } = ExtractedField.Missing<DateOnly?>();

    public IReadOnlyList<ExtractedDeliveryNoteLine> Lines { get; init; } = [];

    /// <summary>
    /// The fields a wrong value in would book the wrong quantity or tie the note to the wrong receipt.
    /// A missing field counts as unreadable (confidence 0), except the PO reference, which a note may not carry.
    /// </summary>
    public IReadOnlyDictionary<string, double> KeyFieldConfidences()
    {
        var confidences = new Dictionary<string, double>
        {
            ["SupplierVatNumber"] = SupplierVatNumber.Confidence ?? 0,
            ["DeliveryNoteNumber"] = DeliveryNoteNumber.Confidence ?? 0,
        };

        if (PurchaseOrderReference.HasValue)
        {
            confidences["PurchaseOrderReference"] = PurchaseOrderReference.Confidence ?? 0;
        }

        foreach (var line in Lines)
        {
            confidences[$"Line{line.LineNumber}.Quantity"] = line.Quantity.Confidence ?? 0;
        }

        return confidences;
    }

    public double MinKeyFieldConfidence() => KeyFieldConfidences().Values.Min();

    /// <summary>The key fields below the threshold, worst first, plus <c>Lines</c> when no line was found at all.</summary>
    public IReadOnlyList<string> LowConfidenceFields(double threshold)
    {
        var fields = KeyFieldConfidences().Where(f => f.Value < threshold).OrderBy(f => f.Value).Select(f => f.Key).ToList();
        if (Lines.Count == 0)
        {
            fields.Add("Lines");
        }

        return fields;
    }
}

public sealed record ExtractedDeliveryNoteLine(
    int LineNumber,
    ExtractedField<string> ItemCode,
    ExtractedField<string> Description,
    ExtractedField<decimal?> Quantity,
    ExtractedField<string> Unit);
