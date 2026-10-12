using System.Globalization;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Domain.Normalisation;

namespace ThreeWayMatch.Infrastructure.Pipeline;

/// <summary>
/// Puts a reviewer's corrections back on top of what the model read. Matching always starts from the stored raw result
/// (so mapping fixes need no new Azure call), and this is how a correction survives that.
/// A corrected field has no printed text to cross-check, and the reviewer is sure: confidence 1.
/// </summary>
public static class CorrectionApplier
{
    /// <summary>The fields a reviewer can correct. Line fields are written <c>Line2.Quantity</c>.</summary>
    public static readonly IReadOnlyList<string> HeaderFields =
        ["InvoiceNumber", "PurchaseOrderReference", "InvoiceDate", "SupplierVatNumber", "NetTotal", "VatTotal", "GrossTotal", "Iban"];

    public static readonly IReadOnlyList<string> LineFields = ["ItemCode", "Quantity", "Unit", "UnitPrice"];

    public static string LineField(int lineNumber, string field) => $"Line{lineNumber}.{field}";

    /// <summary>Checks that a value can be read as the field's type. Amounts use a point, dates are yyyy-MM-dd.</summary>
    public static bool TryValidate(string fieldName, string? text, out string error)
    {
        error = string.Empty;
        var field = fieldName.Contains('.', StringComparison.Ordinal) ? fieldName[(fieldName.IndexOf('.', StringComparison.Ordinal) + 1)..] : fieldName;
        var known = fieldName.Contains('.', StringComparison.Ordinal) ? LineFields.Contains(field) : HeaderFields.Contains(field);
        if (!known)
        {
            error = $"{fieldName} cannot be corrected.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Enter a value.";
            return false;
        }

        switch (field)
        {
            case "InvoiceDate" when !DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _):
                error = "Write the date as yyyy-MM-dd.";
                return false;
            case "NetTotal" or "VatTotal" or "GrossTotal" or "Quantity" or "UnitPrice"
                when !decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out _):
                error = "Write the number with a point, like 1234.50.";
                return false;
            case "SupplierVatNumber" when !Normaliser.TryNormaliseVatNumber(text, out _):
                error = "A VAT number looks like EE123456789.";
                return false;
            case "Iban" when Normaliser.CheckIban(text).Status != IbanStatus.Valid:
                error = "This IBAN is not valid (checksum or format).";
                return false;
            default:
                return true;
        }
    }

    /// <summary>Returns a copy with the corrections applied; the latest correction of a field wins.</summary>
    public static ExtractedInvoice Apply(ExtractedInvoice invoice, IEnumerable<FieldCorrection> corrections)
    {
        var latest = corrections.OrderBy(c => c.CorrectedAt).ThenBy(c => c.Id).GroupBy(c => c.FieldName).ToDictionary(g => g.Key, g => g.Last().CorrectedValue);
        if (latest.Count == 0)
        {
            return invoice;
        }

        string? Header(string name) => latest.GetValueOrDefault(name);

        ExtractedField<string> Text(ExtractedField<string> field, string name) =>
            Header(name) is { } v ? new ExtractedField<string>(v.Trim(), null, 1.0) : field;

        ExtractedField<decimal?> Amount(ExtractedField<decimal?> field, string name) =>
            Header(name) is { } v && decimal.TryParse(v.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? new ExtractedField<decimal?>(d, null, 1.0) : field;

        var date = invoice.InvoiceDate;
        if (Header("InvoiceDate") is { } d
            && DateOnly.TryParseExact(d.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = new ExtractedField<DateOnly?>(parsed, null, 1.0);
        }

        return invoice with
        {
            InvoiceNumber = Text(invoice.InvoiceNumber, "InvoiceNumber"),
            PurchaseOrderReference = Text(invoice.PurchaseOrderReference, "PurchaseOrderReference"),
            SupplierVatNumber = Text(invoice.SupplierVatNumber, "SupplierVatNumber"),
            Iban = Text(invoice.Iban, "Iban"),
            InvoiceDate = date,
            NetTotal = Amount(invoice.NetTotal, "NetTotal"),
            VatTotal = Amount(invoice.VatTotal, "VatTotal"),
            GrossTotal = Amount(invoice.GrossTotal, "GrossTotal"),
            Lines = invoice.Lines.Select(line => line with
            {
                ItemCode = Text(line.ItemCode, LineField(line.LineNumber, "ItemCode")),
                Unit = Text(line.Unit, LineField(line.LineNumber, "Unit")),
                Quantity = Amount(line.Quantity, LineField(line.LineNumber, "Quantity")),
                UnitPrice = Amount(line.UnitPrice, LineField(line.LineNumber, "UnitPrice")),
            }).ToList(),
        };
    }
}
