namespace ThreeWayMatch.Domain.Extraction;

/// <summary>
/// Reads an invoice from a document. The matching engine only knows this interface, so it can be tested
/// with a fake extractor and never depends on Azure.
/// </summary>
public interface IDocumentExtractor
{
    Task<ExtractedInvoice> ExtractInvoiceAsync(Stream document, CancellationToken cancellationToken = default);
}
