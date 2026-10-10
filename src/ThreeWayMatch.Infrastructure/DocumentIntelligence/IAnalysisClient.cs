namespace ThreeWayMatch.Infrastructure.DocumentIntelligence;

/// <summary>
/// Sends a document to Document Intelligence and returns the raw JSON result. Kept apart from the mapping so that
/// the result can be cached, and so the mapping can be tested with saved results and no Azure call.
/// </summary>
internal interface IAnalysisClient
{
    Task<string> AnalyzeInvoiceAsync(ReadOnlyMemory<byte> document, CancellationToken cancellationToken);
}
