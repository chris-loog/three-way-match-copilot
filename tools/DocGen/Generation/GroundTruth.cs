using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ThreeWayMatch.Domain.Ap;

namespace DocGen.Generation;

internal sealed record GtHeader(
    string? InvoiceNumber,
    string? DeliveryNoteNumber,
    string Date,
    string? DueDate,
    string? PoReference,
    decimal? NetTotal,
    decimal? VatTotal,
    decimal? GrossTotal,
    string? Currency,
    string? Iban);

internal sealed record GtLine(
    int LineNumber,
    string SupplierItemCode,
    string Description,
    decimal Quantity,
    string Unit,
    decimal? UnitPrice,
    decimal? VatRate,
    decimal? LineTotal);

internal sealed record GtLinks(string PurchaseOrder, string? GoodsReceipt, string? DuplicateOf);

internal sealed record GtDiscrepancy(
    string Code,
    string Severity,
    int? LineNumber,
    string? Expected,
    string? Actual,
    decimal AmountAtStake);

/// <summary>The answer key for one document: what is printed, what it belongs to, and what is wrong with it.</summary>
internal sealed record GroundTruth(
    string DocumentId,
    string DocumentType,
    string File,
    string Supplier,
    string SupplierVatNumber,
    string Layout,
    string Split,
    string Quality,
    GtHeader Header,
    IReadOnlyList<GtLine> Lines,
    GtLinks Links,
    IReadOnlyList<GtDiscrepancy> InjectedDiscrepancies,
    string ExpectedDecision)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    /// <summary>QtyOverInvoiced becomes QTY_OVER_INVOICED, the spelling used in the discrepancy catalogue.</summary>
    public static string CodeName(DiscrepancyCode code) =>
        Regex.Replace(code.ToString(), "(?<=[a-z])(?=[A-Z])", "_").ToUpperInvariant();

    public static GroundTruth From(PlannedDocument doc, string duplicateOfId)
    {
        var discrepancies = doc.Injected
            .Select(d => new GtDiscrepancy(CodeName(d.Code), d.Severity.ToString(), d.LineNumber, d.Expected, d.Actual, d.AmountAtStake))
            .ToList();

        var links = new GtLinks(doc.PurchaseOrderNumber, doc.GoodsReceiptNumber, duplicateOfId.Length == 0 ? null : duplicateOfId);

        if (doc.Invoice is { } invoice)
        {
            return new GroundTruth(doc.Id, "Invoice", doc.FileName, doc.Supplier.Name, doc.Supplier.VatNumber, doc.Layout.Id,
                doc.Split, doc.Quality,
                new GtHeader(invoice.InvoiceNumber, null, Iso(invoice.InvoiceDate), Iso(invoice.DueDate), invoice.PoReference,
                    invoice.NetTotal, invoice.VatTotal, invoice.GrossTotal, invoice.Currency, invoice.Iban),
                invoice.Lines.Select(l => new GtLine(l.LineNumber, l.Code, l.Description, l.Quantity, l.Unit, l.UnitPrice, l.VatRate, l.LineTotal)).ToList(),
                links, discrepancies, doc.ExpectedDecision.ToString());
        }

        var note = doc.DeliveryNote!;
        return new GroundTruth(doc.Id, "DeliveryNote", doc.FileName, doc.Supplier.Name, doc.Supplier.VatNumber, doc.Layout.Id,
            doc.Split, doc.Quality,
            new GtHeader(null, note.DeliveryNoteNumber, Iso(note.DeliveryDate), null, note.PoReference, null, null, null, null, null),
            note.Lines.Select(l => new GtLine(l.LineNumber, l.Code, l.Description, l.Quantity, l.Unit, null, null, null)).ToList(),
            links, discrepancies, doc.ExpectedDecision.ToString());
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
