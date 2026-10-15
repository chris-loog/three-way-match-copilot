namespace ThreeWayMatch.Infrastructure.DocumentIntelligence;

public sealed class DocumentIntelligenceOptions
{
    /// <summary>For example <c>https://di-three-way-match.cognitiveservices.azure.com/</c>. Not a secret: access is by Entra ID.</summary>
    public required Uri Endpoint { get; init; }

    public string InvoiceModelId { get; init; } = "prebuilt-invoice";

    /// <summary>
    /// The model that reads delivery notes: the custom model's id (for example <c>delivery-note-neural-v1</c>), or
    /// <c>prebuilt-layout</c>, which reads text and tables and leaves the fields to simple rules in <c>DeliveryNoteMapper</c>.
    /// </summary>
    public string DeliveryNoteModelId { get; init; } = "prebuilt-layout";

    /// <summary>
    /// Folder for cached raw results, one file per document content hash and model. Null turns the cache off.
    /// Re-running with the same file never calls Azure again.
    /// </summary>
    public string? CacheDirectory { get; init; }

    /// <summary>
    /// Never call Azure: a document that is not in the cache fails with an error. For evaluation runs that must not
    /// touch the service (and need no <c>az login</c>). Needs <see cref="CacheDirectory"/>.
    /// </summary>
    public bool CacheOnly { get; init; }

    /// <summary>
    /// The free tier allows 20 calls a minute. Three seconds between calls stays safely under that.
    /// Zero turns throttling off (paid tier).
    /// </summary>
    public TimeSpan MinimumCallInterval { get; init; } = TimeSpan.FromSeconds(3);
}
