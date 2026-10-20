using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ThreeWayMatch.Domain.Ap;

namespace ThreeWayMatch.Domain.Explanation;

public sealed record FactFinding(string Code, string Severity, int? Line, string? Expected, string? Actual, decimal AmountAtStake)
{
    /// <summary>What the code means, from <see cref="FindingCatalogue"/>. Sent to the model so it does not guess from the name.</summary>
    public string Meaning => FindingCatalogue.Meaning(Code);
}

/// <summary>A document line as the model sees it. The description is free text from the document: it is truncated and sent as data only.</summary>
public sealed record FactLine(int Line, string? Description, decimal? Quantity, string? Unit, decimal? UnitPrice, string Match);

public sealed record SupplierHistory(int InvoicesLastSixMonths, IReadOnlyDictionary<string, int> EarlierFindings);

/// <summary>
/// Everything the model may talk about, built in C# from the database. The model gets this and nothing else:
/// never the raw document text. The guards check the model's answer against this same object.
/// </summary>
public sealed record ExplanationFacts(
    string DocumentType,
    string? DocumentNumber,
    string? Supplier,
    string SupplierLanguage,
    string? PurchaseOrder,
    string? Currency,
    decimal? GrossTotal,
    string Decision,
    decimal AmountAtStake,
    IReadOnlyList<FactFinding> Findings,
    IReadOnlyList<FactLine> Lines,
    SupplierHistory History)
{
    /// <summary>False when any finding is one the supplier must not be written about (a bank account change, goods still to arrive).</summary>
    public bool SupplierDraftAllowed => Findings.All(f => FindingCatalogue.AllowsSupplierDraft(f.Code));

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>The numbers that appear anywhere in the facts, as values. The number guard compares the answer with this set.</summary>
    public IReadOnlySet<decimal> Numbers() => NumberTokens.FromFactsJson(ToJson());

    /// <summary>Free text from a document, made safe to quote: control characters removed, whitespace collapsed, cut to <paramref name="maxLength"/>.</summary>
    public static string? Clean(string? text, int maxLength = 60)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = Regex.Replace(new string(text.Where(c => !char.IsControl(c) || c == ' ').ToArray()), @"\s+", " ").Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    /// <summary>The language of the message to the supplier: Estonian for et, English for everything else (the only languages the letter frame exists in).</summary>
    public static string Language(string? documentCulture) =>
        string.Equals(documentCulture?.Split('-')[0], "et", StringComparison.OrdinalIgnoreCase) ? "Estonian" : "English";
}

/// <summary>The spelling of discrepancy codes in the catalogue and in the facts: <c>QtyOverInvoiced</c> becomes <c>QTY_OVER_INVOICED</c>.</summary>
public static class CodeNames
{
    public static string Of(DiscrepancyCode code) => Regex.Replace(code.ToString(), "(?<=[a-z])(?=[A-Z])", "_").ToUpperInvariant();
}

/// <summary>Finds numbers in text and turns them into comparable values, whatever the thousand and decimal separators.</summary>
internal static partial class NumberTokens
{
    /// <summary>The numbers of the facts JSON. The JSON writes decimals with a point and no grouping, so parsing is plain.</summary>
    public static IReadOnlySet<decimal> FromFactsJson(string json)
    {
        var values = new HashSet<decimal>();
        foreach (Match match in Token().Matches(json))
        {
            foreach (var candidate in Candidates(match.Value))
            {
                values.Add(candidate);
            }
        }

        return values;
    }

    /// <summary>
    /// The numbers in a model answer that are not in <paramref name="allowed"/>. A token with one separator may be a decimal or a
    /// grouped number ("1,292" is 1.292 in Estonian and 1292 in English), so it passes if either reading is a known number.
    /// A token with several separators ("16.04.2026", "PO-2026-0003") is read part by part.
    /// </summary>
    public static IReadOnlyList<string> Unknown(string text, IReadOnlySet<decimal> allowed)
    {
        var unknown = new List<string>();
        foreach (Match match in Token().Matches(Regroup().Replace(text, string.Empty)))
        {
            if (!Candidates(match.Value).Any(allowed.Contains) && !allowed.Contains(WholeNumber(match.Value)))
            {
                unknown.Add(match.Value);
            }
        }

        return unknown;
    }

    private static decimal WholeNumber(string token) =>
        decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : decimal.MinValue;

    private static IEnumerable<decimal> Candidates(string token)
    {
        var parts = token.Split(['.', ','], StringSplitOptions.RemoveEmptyEntries);
        var separators = token.Count(c => c is '.' or ',');

        if (separators == 0)
        {
            yield return decimal.Parse(token, CultureInfo.InvariantCulture);
        }
        else if (separators == 1)
        {
            yield return decimal.Parse(parts[0] + "." + (parts.Length > 1 ? parts[1] : "0"), CultureInfo.InvariantCulture);
            if (parts.Length == 2 && parts[1].Length == 3)
            {
                yield return decimal.Parse(parts[0] + parts[1], CultureInfo.InvariantCulture);
            }
        }
        else
        {
            foreach (var part in parts)
            {
                yield return decimal.Parse(part, CultureInfo.InvariantCulture);
            }
        }
    }

    // "1 292,50" and "1 292 000": a space or no-break space between digit groups is a thousand separator. Removed before splitting into tokens.
    [GeneratedRegex(@"(?<=\d)[   ](?=\d{3}(?!\d))")]
    private static partial Regex Regroup();

    [GeneratedRegex(@"\d+(?:[.,]\d+)*")]
    private static partial Regex Token();
}
