using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Domain.Normalisation;

namespace ThreeWayMatch.Domain.Matching;

/// <summary>One line of a document to match. Invoices and delivery notes both reduce to this.</summary>
public sealed record MatchInputLine(
    int LineNumber,
    string? ItemCode,
    string? Description,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice);

/// <summary>
/// What the matching engine needs to know about a document. The engine never sees Azure types or raw extraction results:
/// an adapter turns each document type into this.
/// </summary>
public sealed record MatchInput(
    string? SupplierVatNumber,
    string? SupplierName,
    string? PurchaseOrderReference,
    IReadOnlyList<MatchInputLine> Lines)
{
    public static MatchInput FromInvoice(ExtractedInvoice invoice)
    {
        var vat = invoice.SupplierVatNumber.Value;
        return new MatchInput(
            SupplierVatNumber: vat is not null && Normaliser.TryNormaliseVatNumber(vat, out var normalised) ? normalised : vat,
            SupplierName: invoice.SupplierName.Value,
            PurchaseOrderReference: invoice.PurchaseOrderReference.Value,
            Lines: invoice.Lines
                .Select(l => new MatchInputLine(l.LineNumber, l.ItemCode.Value, l.Description.Value, l.Quantity.Value, l.Unit.Value, l.UnitPrice.Value))
                .ToList());
    }

    public static MatchInput FromDeliveryNote(ExtractedDeliveryNote note)
    {
        var vat = note.SupplierVatNumber.Value;
        return new MatchInput(
            SupplierVatNumber: vat is not null && Normaliser.TryNormaliseVatNumber(vat, out var normalised) ? normalised : vat,
            SupplierName: note.SupplierName.Value,
            PurchaseOrderReference: note.PurchaseOrderReference.Value,
            Lines: note.Lines
                .Select(l => new MatchInputLine(l.LineNumber, l.ItemCode.Value, l.Description.Value, l.Quantity.Value, l.Unit.Value, null))
                .ToList());
    }
}
