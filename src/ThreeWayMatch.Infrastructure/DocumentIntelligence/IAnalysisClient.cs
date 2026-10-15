namespace ThreeWayMatch.Infrastructure.DocumentIntelligence;

/// <summary>
/// Sends a document to one Document Intelligence model and returns the raw JSON result. Kept apart from the mapping so that
/// the result can be cached, and so the mapping can be tested with saved results and no Azure call.
/// </summary>
internal interface IAnalysisClient
{
    Task<string> AnalyzeAsync(ReadOnlyMemory<byte> document, CancellationToken cancellationToken);
}
