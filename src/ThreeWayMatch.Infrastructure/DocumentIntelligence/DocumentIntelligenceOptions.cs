namespace ThreeWayMatch.Infrastructure.DocumentIntelligence;

public sealed class DocumentIntelligenceOptions
{
    /// <summary>For example <c>https://di-three-way-match.cognitiveservices.azure.com/</c>. Not a secret: access is by Entra ID.</summary>
    public required Uri Endpoint { get; init; }

    public string InvoiceModelId { get; init; } = "prebuilt-invoice";

    /// <summary>
    /// Folder for cached raw results, one file per document content hash and model. Null turns the cache off.
    /// Re-running with the same file never calls Azure again.
    /// </summary>
    public string? CacheDirectory { get; init; }

    /// <summary>
    /// The free tier allows 20 calls a minute. Three seconds between calls stays safely under that.
    /// Zero turns throttling off (paid tier).
    /// </summary>
    public TimeSpan MinimumCallInterval { get; init; } = TimeSpan.FromSeconds(3);
}
