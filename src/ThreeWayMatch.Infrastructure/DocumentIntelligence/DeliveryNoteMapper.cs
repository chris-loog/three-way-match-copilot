using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.AI.DocumentIntelligence;
using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Domain.Normalisation;

namespace ThreeWayMatch.Infrastructure.DocumentIntelligence;

/// <summary>
/// Turns a raw Document Intelligence result into an <see cref="ExtractedDeliveryNote"/>. Pure, like <see cref="InvoiceMapper"/>.
/// Two sources, chosen by what the result holds:
/// the custom model's fields (<c>DeliveryNoteNumber</c>, <c>Lines</c>, ...), or, when there are none, the plain
/// <c>prebuilt-layout</c> text and tables read with simple rules (the fallback until a custom model is trained).
/// The confidence of a value found by the rules is the lowest confidence of the words it was read from.
/// </summary>
internal static partial class DeliveryNoteMapper
{
    /// <summary>
    /// The buyer's own VAT number. It is printed on every delivery note, and it is not the supplier's.
    /// The company in this project is fictional, so one constant is enough.
    /// </summary>
    public const string OwnVatNumber = "EE100000001";

    public static ExtractedDeliveryNote Map(string rawJson, string modelId)
    {
        var result = InvoiceMapper.Read(rawJson);
        var fields = result.Documents.Count > 0 ? result.Documents[0].Fields : null;

        return fields is { Count: > 0 }
            ? FromModelFields(rawJson, modelId, fields)
            : FromLayout(rawJson, modelId);
    }

    // ---- custom model ----

    private static ExtractedDeliveryNote FromModelFields(string rawJson, string modelId, DocumentFieldDictionary fields) => new()
    {
        ModelId = modelId,
        RawJson = rawJson,
        SupplierName = InvoiceMapper.Text(fields, "SupplierName"),
        SupplierVatNumber = InvoiceMapper.Text(fields, "SupplierVatNumber"),
        DeliveryNoteNumber = InvoiceMapper.Text(fields, "DeliveryNoteNumber"),
        PurchaseOrderReference = InvoiceMapper.Text(fields, "PONumber"),
        DeliveryDate = InvoiceMapper.Date(fields, "DeliveryDate"),
        Lines = ModelLines(fields),
    };

    private static List<ExtractedDeliveryNoteLine> ModelLines(DocumentFieldDictionary fields)
    {
        var lines = new List<ExtractedDeliveryNoteLine>();
        if (!fields.TryGetValue("Lines", out var items) || items.ValueList is null)
        {
            return lines;
        }

        foreach (var item in items.ValueList)
        {
            var line = item.ValueDictionary;
            var quantity = InvoiceMapper.Number(line, "Quantity");
            if (!quantity.HasValue && line is not null && line.TryGetValue("Quantity", out var raw))
            {
                quantity = ParsedQuantity(raw.Content, raw.Confidence);
            }

            lines.Add(new ExtractedDeliveryNoteLine(
                LineNumber: lines.Count + 1,
                ItemCode: InvoiceMapper.Text(line, "ItemCode"),
                Description: InvoiceMapper.Text(line, "Description"),
                Quantity: quantity,
                Unit: InvoiceMapper.Text(line, "Unit")));
        }

        return lines;
    }

    // ---- prebuilt-layout and rules ----

    private static ExtractedDeliveryNote FromLayout(string rawJson, string modelId)
    {
        using var document = JsonDocument.Parse(rawJson);
        var root = document.RootElement.TryGetProperty("analyzeResult", out var inner) ? inner : document.RootElement;
        var content = root.TryGetProperty("content", out var c) ? c.GetString() ?? string.Empty : string.Empty;
        var words = ReadWords(root);

        var dateMatch = DateAfterLabel().Match(content);
        var dateField = Located(dateMatch, content, words);
        var culture = CultureInfo.GetCultureInfo(dateMatch.Groups["label"].Value.StartsWith("delivery", StringComparison.OrdinalIgnoreCase) ? "en-GB" : "et-EE");
        var date = Normaliser.TryParseDate(dateField.Value, culture, out var parsed)
            ? new ExtractedField<DateOnly?>(parsed, dateField.RawText, dateField.Confidence)
            : ExtractedField.Missing<DateOnly?>();

        return new ExtractedDeliveryNote
        {
            ModelId = modelId,
            RawJson = rawJson,
            SupplierName = Located(SupplierFooter().Match(content), content, words),
            SupplierVatNumber = SupplierVat(content, words),
            DeliveryNoteNumber = Located(NumberAfterLabel().Match(content), content, words),
            PurchaseOrderReference = Located(PurchaseOrderToken().Match(content), content, words),
            DeliveryDate = date,
            Lines = TableLines(root, words),
        };
    }

    /// <summary>The first VAT number that is not the buyer's own.</summary>
    private static ExtractedField<string> SupplierVat(string content, IReadOnlyList<Word> words)
    {
        foreach (Match match in VatNumber().Matches(content))
        {
            if (!string.Equals(match.Groups["value"].Value.Replace(" ", string.Empty, StringComparison.Ordinal), OwnVatNumber, StringComparison.OrdinalIgnoreCase))
            {
                return Located(match, content, words);
            }
        }

        return ExtractedField.Missing<string>();
    }

    private static List<ExtractedDeliveryNoteLine> TableLines(JsonElement root, IReadOnlyList<Word> words)
    {
        var lines = new List<ExtractedDeliveryNoteLine>();
        if (!root.TryGetProperty("tables", out var tables))
        {
            return lines;
        }

        foreach (var table in tables.EnumerateArray())
        {
            var cells = table.GetProperty("cells").EnumerateArray()
                .Select(cell => new Cell(
                    cell.GetProperty("rowIndex").GetInt32(),
                    cell.GetProperty("columnIndex").GetInt32(),
                    (cell.TryGetProperty("content", out var text) ? text.GetString() : null)?.Trim() ?? string.Empty,
                    ReadSpans(cell)))
                .ToList();

            var columns = HeaderColumns(cells);
            if (!columns.ContainsKey(Column.Quantity))
            {
                continue;
            }

            var rowCount = cells.Max(x => x.Row);
            for (var row = 1; row <= rowCount; row++)
            {
                var line = RowLine(cells.Where(x => x.Row == row).ToList(), columns, lines.Count + 1, words);
                if (line is not null)
                {
                    lines.Add(line);
                }
            }

            break;
        }

        return lines;
    }

    private static ExtractedDeliveryNoteLine? RowLine(List<Cell> row, Dictionary<Column, int> columns, int number, IReadOnlyList<Word> words)
    {
        ExtractedField<string> Text(Column column)
        {
            var cell = columns.TryGetValue(column, out var index) ? row.FirstOrDefault(x => x.Column == index) : null;
            return cell is null || cell.Content.Length == 0
                ? ExtractedField.Missing<string>()
                : new ExtractedField<string>(cell.Content, cell.Content, MinConfidence(cell.Spans, words));
        }

        var quantityCell = row.FirstOrDefault(x => x.Column == columns[Column.Quantity]);
        var code = Text(Column.ItemCode);
        var description = Text(Column.Description);
        if ((quantityCell is null || quantityCell.Content.Length == 0) && !code.HasValue && !description.HasValue)
        {
            return null;
        }

        var quantity = quantityCell is null
            ? ExtractedField.Missing<decimal?>()
            : ParsedQuantity(quantityCell.Content, MinConfidence(quantityCell.Spans, words));

        return new ExtractedDeliveryNoteLine(number, code, description, quantity, Text(Column.Unit));
    }

    private static Dictionary<Column, int> HeaderColumns(List<Cell> cells)
    {
        var columns = new Dictionary<Column, int>();
        foreach (var header in cells.Where(x => x.Row == 0))
        {
            if (HeaderNames.TryGetValue(header.Content.Trim().TrimEnd('.').ToLowerInvariant(), out var column))
            {
                columns.TryAdd(column, header.Column);
            }
        }

        return columns;
    }

    private enum Column { ItemCode, Description, Quantity, Unit }

    // Column titles seen on the four supplier layouts (Estonian and English).
    private static readonly Dictionary<string, Column> HeaderNames = new()
    {
        ["kood"] = Column.ItemCode, ["artikkel"] = Column.ItemCode, ["item code"] = Column.ItemCode, ["item"] = Column.ItemCode,
        ["art. nr"] = Column.ItemCode, ["art nr"] = Column.ItemCode,
        ["nimetus"] = Column.Description, ["kirjeldus"] = Column.Description, ["description"] = Column.Description,
        ["kogus"] = Column.Quantity, ["qty"] = Column.Quantity, ["quantity"] = Column.Quantity,
        ["ühik"] = Column.Unit, ["unit"] = Column.Unit,
    };

    /// <summary>
    /// A decimal comma means Estonian number format; otherwise the English one. Thousand separators (spaces) work in both.
    /// A text that does not parse gives no value, with confidence 0, so it is flagged instead of silently read as something else.
    /// </summary>
    private static ExtractedField<decimal?> ParsedQuantity(string? text, double? confidence)
    {
        var culture = CultureInfo.GetCultureInfo(text is not null && text.Contains(',', StringComparison.Ordinal) ? "et-EE" : "en-GB");
        return Normaliser.TryParseAmount(text, culture, out var value)
            ? new ExtractedField<decimal?>(value, text, confidence)
            : new ExtractedField<decimal?>(null, text, 0);
    }

    private static ExtractedField<string> Located(Match match, string content, IReadOnlyList<Word> words)
    {
        if (!match.Success)
        {
            return ExtractedField.Missing<string>();
        }

        var group = match.Groups["value"];
        return new ExtractedField<string>(group.Value.Trim(), group.Value.Trim(), MinConfidence([new Span(group.Index, group.Length)], words));
    }

    private static double? MinConfidence(IEnumerable<Span> spans, IReadOnlyList<Word> words)
    {
        var confidences = spans
            .SelectMany(span => words.Where(w => w.Offset < span.Offset + span.Length && w.Offset + w.Length > span.Offset))
            .Select(w => w.Confidence)
            .ToList();
        return confidences.Count > 0 ? confidences.Min() : null;
    }

    private static List<Word> ReadWords(JsonElement root)
    {
        var words = new List<Word>();
        if (!root.TryGetProperty("pages", out var pages))
        {
            return words;
        }

        foreach (var page in pages.EnumerateArray().Where(p => p.TryGetProperty("words", out _)))
        {
            foreach (var word in page.GetProperty("words").EnumerateArray())
            {
                var span = word.GetProperty("span");
                words.Add(new Word(span.GetProperty("offset").GetInt32(), span.GetProperty("length").GetInt32(), word.GetProperty("confidence").GetDouble()));
            }
        }

        return words;
    }

    private static List<Span> ReadSpans(JsonElement cell) =>
        cell.TryGetProperty("spans", out var spans)
            ? spans.EnumerateArray().Select(s => new Span(s.GetProperty("offset").GetInt32(), s.GetProperty("length").GetInt32())).ToList()
            : [];

    private readonly record struct Word(int Offset, int Length, double Confidence);

    private readonly record struct Span(int Offset, int Length);

    private sealed record Cell(int Row, int Column, string Content, List<Span> Spans);

    // "Saatelehe nr: 2959", "Delivery note no.: 3398". The OCR sometimes puts a space before the dot or colon: "no .: 3399".
    [GeneratedRegex(@"(?:saatelehe\s*nr|delivery\s*note\s*no)\b(?:\s*[.:])*\s*(?<value>[A-Za-z0-9][A-Za-z0-9\-/]*)", RegexOptions.IgnoreCase)]
    private static partial Regex NumberAfterLabel();

    // The first "Tarnekuupäev"/"Delivery date" with a date after it; the signature box at the bottom repeats the label with nothing after it.
    [GeneratedRegex(@"(?<label>tarnekuup[äa]ev|delivery\s*date)\s*:?\s*(?<value>\d{1,4}[./-]\d{1,2}[./-]\d{1,4}|\d{1,2}\s+[A-Za-z]{3,9}\s+\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex DateAfterLabel();

    // "PO-2026-0001", and "P0-" for a misread letter O.
    [GeneratedRegex(@"\b(?<value>P[O0]-\d{4}-\d{3,5})\b")]
    private static partial Regex PurchaseOrderToken();

    [GeneratedRegex(@"\b(?<value>EE\s?\d{9})\b")]
    private static partial Regex VatNumber();

    // Every layout ends with "<supplier> · KMKR nr EE… · 1 / 1" or "<supplier> · VAT no. EE… · 1 / 1".
    [GeneratedRegex(@"^(?<value>[^\n·]+?)\s·\s(?:KMKR nr|VAT no\.)", RegexOptions.Multiline)]
    private static partial Regex SupplierFooter();
}
