using System.Globalization;
using ThreeWayMatch.Domain.Extraction;

namespace ThreeWayMatch.Domain.Normalisation;

/// <summary>
/// Reads the printed text of each key field with our own parser and compares it with the typed value the model returned.
/// Where they disagree, or our parser cannot read the text, or an identifier is not well formed, the field's confidence
/// drops to 0, so the document is flagged <c>LOW_CONFIDENCE</c> and goes to review instead of being trusted.
/// </summary>
public static class ExtractionCrossCheck
{
    private const decimal AmountTolerance = 0.0001m;

    /// <summary>Returns a corrected copy. The invoice passed in is not changed.</summary>
    public static ExtractedInvoice Apply(ExtractedInvoice invoice, CultureInfo culture) => invoice with
    {
        InvoiceDate = CheckDate(invoice.InvoiceDate, culture),
        NetTotal = CheckAmount(invoice.NetTotal, culture),
        VatTotal = CheckAmount(invoice.VatTotal, culture),
        GrossTotal = CheckAmount(invoice.GrossTotal, culture),
        SupplierVatNumber = CheckVat(invoice.SupplierVatNumber),
        Iban = CheckIban(invoice.Iban),
        Lines = invoice.Lines.Select(line => line with
        {
            Quantity = CheckAmount(line.Quantity, culture),
            UnitPrice = CheckAmount(line.UnitPrice, culture),
            LineTotal = CheckAmount(line.LineTotal, culture),
        }).ToList(),
    };

    private static ExtractedField<T> Distrust<T>(ExtractedField<T> field) => field with { Confidence = 0 };

    private static ExtractedField<decimal?> CheckAmount(ExtractedField<decimal?> field, CultureInfo culture)
    {
        // Without the source text there is nothing to compare with, so the model's own confidence stands.
        if (field.Value is not { } typed || field.RawText is null)
        {
            return field;
        }

        return TryParseWithUnitSuffix(field.RawText, culture, out var own) && Math.Abs(own - typed) < AmountTolerance
            ? field
            : Distrust(field);
    }

    /// <summary>
    /// A quantity's source text can include its unit ("13,53 m³"). If the plain parse fails, retry without trailing
    /// non-digits. Text with a letter in the middle ("12a3") still fails.
    /// </summary>
    private static bool TryParseWithUnitSuffix(string raw, CultureInfo culture, out decimal value) =>
        Normaliser.TryParseAmount(raw, culture, out value)
        || Normaliser.TryParseAmount(raw.TrimEnd(raw.Reverse().TakeWhile(c => c is not (>= '0' and <= '9')).ToArray()), culture, out value);

    private static ExtractedField<DateOnly?> CheckDate(ExtractedField<DateOnly?> field, CultureInfo culture)
    {
        if (field.Value is not { } typed || field.RawText is null)
        {
            return field;
        }

        return Normaliser.TryParseDate(field.RawText, culture, out var own) && own == typed ? field : Distrust(field);
    }

    private static ExtractedField<string> CheckVat(ExtractedField<string> field)
    {
        var text = field.RawText ?? field.Value;
        return text is null || Normaliser.TryNormaliseVatNumber(text, out _) ? field : Distrust(field);
    }

    private static ExtractedField<string> CheckIban(ExtractedField<string> field)
    {
        var text = field.RawText ?? field.Value;
        return text is null || Normaliser.CheckIban(text).Status == IbanStatus.Valid ? field : Distrust(field);
    }
}
