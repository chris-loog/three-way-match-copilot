using ThreeWayMatch.Domain.Extraction;

namespace ThreeWayMatch.Infrastructure.DocumentIntelligence;

/// <summary>Reads invoices with one model and delivery notes with another; each has its own analysis client (and cache key).</summary>
internal sealed class DocumentIntelligenceExtractor(
    IAnalysisClient invoiceAnalysis, string invoiceModelId,
    IAnalysisClient deliveryNoteAnalysis, string deliveryNoteModelId) : IDocumentExtractor
{
    public async Task<ExtractedInvoice> ExtractInvoiceAsync(Stream document, CancellationToken cancellationToken = default)
    {
        var json = await invoiceAnalysis.AnalyzeAsync(await ReadAsync(document, cancellationToken), cancellationToken);
        return InvoiceMapper.Map(json, invoiceModelId);
    }

    public async Task<ExtractedDeliveryNote> ExtractDeliveryNoteAsync(Stream document, CancellationToken cancellationToken = default)
    {
        var json = await deliveryNoteAnalysis.AnalyzeAsync(await ReadAsync(document, cancellationToken), cancellationToken);
        return DeliveryNoteMapper.Map(json, deliveryNoteModelId);
    }

    private static async Task<byte[]> ReadAsync(Stream document, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await document.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}
