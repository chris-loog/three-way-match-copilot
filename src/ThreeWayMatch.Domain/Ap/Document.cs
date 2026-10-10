namespace ThreeWayMatch.Domain.Ap;

public enum DocumentType
{
    Invoice,
    DeliveryNote,
}

public enum DocumentStatus
{
    Received,
    Extracted,
    Matched,
    AutoApproved,
    OnHold,
    NeedsReview,
    Rejected,
    Failed,
}

/// <summary>An uploaded file. The blob stays where it was uploaded; this row tracks where it is in the pipeline.</summary>
public class Document
{
    public int Id { get; set; }
    public required string BlobPath { get; set; }
    public DocumentType Type { get; set; }
    public DocumentStatus Status { get; set; }

    /// <summary>SHA-256 of the file content as 64 hex characters. Unique: the same file is never processed twice.</summary>
    public required string ContentHash { get; set; }

    public string? ErrorMessage { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }

    public ExtractedDocument? Extracted { get; set; }
    public MatchResult? MatchResult { get; set; }
}
