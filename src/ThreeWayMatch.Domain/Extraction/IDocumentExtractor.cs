namespace ThreeWayMatch.Domain.Extraction;

/// <summary>
/// Reads a document. The matching engine only knows this interface, so it can be tested
/// with a fake extractor and never depends on Azure.
/// </summary>
public interface IDocumentExtractor
{
    Task<ExtractedInvoice> ExtractInvoiceAsync(Stream document, CancellationToken cancellationToken = default);

    Task<ExtractedDeliveryNote> ExtractDeliveryNoteAsync(Stream document, CancellationToken cancellationToken = default);
}
