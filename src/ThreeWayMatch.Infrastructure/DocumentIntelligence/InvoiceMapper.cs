using System.ClientModel.Primitives;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.AI.DocumentIntelligence;
using ThreeWayMatch.Domain.Extraction;

namespace ThreeWayMatch.Infrastructure.DocumentIntelligence;

/// <summary>
/// Turns a raw Document Intelligence result into an <see cref="ExtractedInvoice"/>. Pure: it reads the JSON and
/// nothing else, so tests feed it saved results. It does not clean values up: parsing and normalising come in step 3.2.
/// </summary>
internal static partial class InvoiceMapper
{
    public static ExtractedInvoice Map(string rawJson, string modelId)
    {
        var result = Read(rawJson);
        var fields = result.Documents.Count > 0 ? result.Documents[0].Fields : null;

        var money = (string name) => Money(fields, name);
        var gross = money("InvoiceTotal");

        return new ExtractedInvoice
        {
            ModelId = modelId,
            RawJson = rawJson,
            SupplierName = Text(fields, "VendorName"),
            SupplierVatNumber = Text(fields, "VendorTaxId"),
            SupplierRegistryCode = Prefer(Text(fields, "RegistryCode"), () => FindByRegex(result, RegistryCodePattern())),
            InvoiceNumber = Text(fields, "InvoiceId"),
            PurchaseOrderReference = Text(fields, "PurchaseOrder"),
            InvoiceDate = Date(fields, "InvoiceDate"),
            NetTotal = money("SubTotal"),
            VatTotal = money("TotalTax"),
            GrossTotal = gross.HasValue ? gross : money("AmountDue"),
            Currency = CurrencyCode(fields),
            Iban = Prefer(Text(fields, "Iban"), () => FindByRegex(result, IbanPattern())),
            Lines = MapLines(fields),
        };
    }

    /// <summary>Reads the result JSON. Accepts both the bare result and the Studio export that wraps it in <c>analyzeResult</c>.</summary>
    private static AnalyzeResult Read(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        var json = document.RootElement.TryGetProperty("analyzeResult", out var inner) ? inner.GetRawText() : rawJson;
        return ModelReaderWriter.Read<AnalyzeResult>(BinaryData.FromString(json))
            ?? throw new InvalidOperationException("The Document Intelligence result could not be read.");
    }

    private static List<ExtractedInvoiceLine> MapLines(DocumentFieldDictionary? fields)
    {
        var lines = new List<ExtractedInvoiceLine>();
        if (fields is null || !fields.TryGetValue("Items", out var items) || items.ValueList is null)
        {
            return lines;
        }

        foreach (var item in items.ValueList)
        {
            var line = item.ValueDictionary;
            lines.Add(new ExtractedInvoiceLine(
                LineNumber: lines.Count + 1,
                ItemCode: Text(line, "ProductCode"),
                Description: Text(line, "Description"),
                Quantity: Number(line, "Quantity"),
                Unit: Text(line, "Unit"),
                UnitPrice: Money(line, "UnitPrice"),
                VatRate: TaxRate(line, "TaxRate"),
                LineTotal: Money(line, "Amount")));
        }

        return lines;
    }

    private static ExtractedField<string> Text(DocumentFieldDictionary? fields, string name) =>
        fields is not null && fields.TryGetValue(name, out var field)
            ? new ExtractedField<string>(field.ValueString ?? field.Content, field.Content, field.Confidence)
            : ExtractedField.Missing<string>();

    private static ExtractedField<DateOnly?> Date(DocumentFieldDictionary? fields, string name)
    {
        if (fields is null || !fields.TryGetValue(name, out var field))
        {
            return ExtractedField.Missing<DateOnly?>();
        }

        DateOnly? value = field.ValueDate is { } date ? DateOnly.FromDateTime(date.DateTime) : null;
        return new ExtractedField<DateOnly?>(value, field.Content, field.Confidence);
    }

    private static ExtractedField<decimal?> Money(DocumentFieldDictionary? fields, string name)
    {
        if (fields is null || !fields.TryGetValue(name, out var field))
        {
            return ExtractedField.Missing<decimal?>();
        }

        double? amount = field.ValueCurrency?.Amount ?? field.ValueDouble;
        return new ExtractedField<decimal?>(amount is { } a ? (decimal)a : null, field.Content, field.Confidence);
    }

    private static ExtractedField<decimal?> Number(DocumentFieldDictionary? fields, string name)
    {
        if (fields is null || !fields.TryGetValue(name, out var field))
        {
            return ExtractedField.Missing<decimal?>();
        }

        double? number = field.ValueDouble ?? (field.ValueInt64 is { } whole ? whole : null);
        return new ExtractedField<decimal?>(number is { } n ? (decimal)n : null, field.Content, field.Confidence);
    }

    /// <summary>The model returns the tax rate as text such as "24%". This turns it into 0.24; anything odd stays missing.</summary>
    private static ExtractedField<decimal?> TaxRate(DocumentFieldDictionary? fields, string name)
    {
        if (fields is null || !fields.TryGetValue(name, out var field))
        {
            return ExtractedField.Missing<decimal?>();
        }

        var text = (field.ValueString ?? field.Content ?? string.Empty).Replace("%", string.Empty, StringComparison.Ordinal).Replace(',', '.').Trim();
        decimal? rate = decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var percent) ? percent / 100m : null;
        return new ExtractedField<decimal?>(rate, field.Content, field.Confidence);
    }

    private static ExtractedField<string> CurrencyCode(DocumentFieldDictionary? fields)
    {
        if (fields is null)
        {
            return ExtractedField.Missing<string>();
        }

        foreach (var name in new[] { "InvoiceTotal", "AmountDue", "SubTotal", "TotalTax" })
        {
            if (fields.TryGetValue(name, out var field) && field.ValueCurrency?.CurrencyCode is { Length: > 0 } code)
            {
                return new ExtractedField<string>(code, field.ValueCurrency.CurrencySymbol, field.Confidence);
            }
        }

        return ExtractedField.Missing<string>();
    }

    private static ExtractedField<string> Prefer(ExtractedField<string> fromModel, Func<ExtractedField<string>> fallback) =>
        fromModel.HasValue ? fromModel : fallback();

    /// <summary>
    /// Looks for a value in the page text when the model did not return it. The confidence of such a value is the
    /// lowest confidence of the words it was read from, so a smudged IBAN still counts as an unsure one.
    /// </summary>
    private static ExtractedField<string> FindByRegex(AnalyzeResult result, Regex pattern)
    {
        var match = pattern.Match(result.Content ?? string.Empty);
        if (!match.Success)
        {
            return ExtractedField.Missing<string>();
        }

        var group = match.Groups["value"];
        var overlapping = result.Pages
            .SelectMany(page => page.Words)
            .Where(word => word.Span.Offset < group.Index + group.Length && word.Span.Offset + word.Span.Length > group.Index)
            .Select(word => (double)word.Confidence)
            .ToList();

        return new ExtractedField<string>(group.Value, group.Value, overlapping.Count > 0 ? overlapping.Min() : null);
    }

    // An IBAN: two letters, two digits, then groups of four letters or digits (spaces allowed), maybe a short last group.
    [GeneratedRegex(@"\b(?<value>[A-Z]{2}\d{2}(?:[ ]?[A-Z0-9]{4}){3,7}(?:[ ]?[A-Z0-9]{1,3})?)\b")]
    private static partial Regex IbanPattern();

    // "Registrikood 11812518", "Reg. code 10776889", "Reg. 10000001": an eight digit number after a registry label.
    [GeneratedRegex(@"(?:registrikood|reg\.?\s*code|reg\.?(?:\s*no\.?)?)\s*[:.]?\s*(?<value>\d{8})\b", RegexOptions.IgnoreCase)]
    private static partial Regex RegistryCodePattern();
}
