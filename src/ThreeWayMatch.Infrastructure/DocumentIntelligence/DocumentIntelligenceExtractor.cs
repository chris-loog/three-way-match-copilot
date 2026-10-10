using ThreeWayMatch.Domain.Extraction;

namespace ThreeWayMatch.Infrastructure.DocumentIntelligence;

internal sealed class DocumentIntelligenceExtractor(IAnalysisClient analysis, string modelId) : IDocumentExtractor
{
    public async Task<ExtractedInvoice> ExtractInvoiceAsync(Stream document, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await document.CopyToAsync(buffer, cancellationToken);

        var json = await analysis.AnalyzeInvoiceAsync(buffer.ToArray(), cancellationToken);
        return InvoiceMapper.Map(json, modelId);
    }
}
