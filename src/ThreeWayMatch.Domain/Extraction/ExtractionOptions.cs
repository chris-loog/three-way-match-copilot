namespace ThreeWayMatch.Domain.Extraction;

public sealed class ExtractionOptions
{
    /// <summary>
    /// A key field read with less confidence than this makes the document <c>LOW_CONFIDENCE</c>, so it goes to review.
    /// Tuned on the dev split in step 8.2, never on the test split.
    /// </summary>
    public double ConfidenceThreshold { get; init; } = 0.80;
}
