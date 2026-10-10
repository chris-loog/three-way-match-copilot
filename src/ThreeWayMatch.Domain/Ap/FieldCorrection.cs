namespace ThreeWayMatch.Domain.Ap;

/// <summary>
/// A reviewer's correction of an extracted field. Feeds accuracy per supplier and field,
/// learned item aliases, and (later) retraining data.
/// </summary>
public class FieldCorrection
{
    public int Id { get; set; }
    public int ExtractedDocumentId { get; set; }

    /// <summary>Set when the corrected field belongs to a line rather than the header.</summary>
    public int? ExtractedLineId { get; set; }

    public required string FieldName { get; set; }
    public string? ExtractedValue { get; set; }
    public double? ExtractedConfidence { get; set; }
    public string? CorrectedValue { get; set; }
    public required string Reviewer { get; set; }
    public DateTimeOffset CorrectedAt { get; set; }

    public ExtractedDocument? ExtractedDocument { get; set; }
    public ExtractedLine? ExtractedLine { get; set; }
}
