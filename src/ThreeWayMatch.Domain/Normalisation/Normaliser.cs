using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ThreeWayMatch.Domain.Normalisation;

/// <summary>
/// Reads the text printed on a document into clean values. Pure functions with no Azure or database dependency.
/// Nothing here throws on bad input: a value that cannot be read returns false (or a status), and the caller sends
/// the document to review instead of guessing.
/// </summary>
public static partial class Normaliser
{
    private static readonly HashSet<char> SpaceLike = [' ', ' ', ' ', ' '];

    // ---------- amounts ----------

    /// <summary>
    /// Parses an amount the way the supplier wrote it. Spaces (also non-breaking) group thousands; a comma or a point
    /// is the decimal separator. When a single separator is ambiguous ("1.234"), the culture decides: it is the
    /// culture's decimal separator, or else a thousands separator if exactly three digits follow, or else a decimal one.
    /// </summary>
    public static bool TryParseAmount(string? text, CultureInfo culture, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = CurrencyMarks().Replace(text.Replace('−', '-'), string.Empty).Trim();

        var negative = false;
        if (s.Length > 1 && s[0] == '(' && s[^1] == ')')
        {
            negative = true;
            s = s[1..^1].Trim();
        }
        else if (s.StartsWith('-'))
        {
            negative = true;
            s = s[1..].Trim();
        }

        if (s.Length == 0 || s.Any(c => !(IsDigit(c) || c is ',' or '.' || SpaceLike.Contains(c))))
        {
            return false;
        }

        var (decimalSeparator, groupSeparator) = FindSeparators(s, culture);

        var integerPart = s;
        var fraction = string.Empty;
        if (decimalSeparator is { } separator)
        {
            var at = s.LastIndexOf(separator);
            integerPart = s[..at];
            fraction = s[(at + 1)..];
            if (fraction.Length == 0 || !fraction.All(IsDigit))
            {
                return false;
            }
        }

        if (!TryReadGroupedDigits(integerPart, groupSeparator, out var digits) || (digits.Length == 0 && fraction.Length == 0))
        {
            return false;
        }

        var number = (digits.Length == 0 ? "0" : digits) + (fraction.Length > 0 ? "." + fraction : string.Empty);
        if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        if (negative)
        {
            value = -value;
        }

        return true;
    }

    private static (char? Decimal, char? Group) FindSeparators(string s, CultureInfo culture)
    {
        var hasComma = s.Contains(',', StringComparison.Ordinal);
        var hasPoint = s.Contains('.', StringComparison.Ordinal);

        if (hasComma && hasPoint)
        {
            // Both appear: the one that comes last is the decimal separator ("1.234,56" and "1,234.56").
            var decimalIsComma = s.LastIndexOf(',') > s.LastIndexOf('.');
            return decimalIsComma ? (',', '.') : ('.', ',');
        }

        if (!hasComma && !hasPoint)
        {
            return (null, null);
        }

        var separator = hasComma ? ',' : '.';
        if (s.Count(c => c == separator) > 1)
        {
            return (null, separator); // "1.234.567": it can only be a thousands separator
        }

        var digitsAfter = s.Length - s.IndexOf(separator) - 1;
        if (separator == culture.NumberFormat.NumberDecimalSeparator[0])
        {
            return (separator, null);
        }

        return digitsAfter == 3 ? (null, separator) : (separator, null);
    }

    /// <summary>
    /// Checks that an integer part is either plain digits or groups of three separated by spaces or the group separator,
    /// and returns the digits without separators. "1 2 3", "1234 56" and "0.123" are refused: they are ambiguous.
    /// </summary>
    private static bool TryReadGroupedDigits(string integerPart, char? groupSeparator, out string digits)
    {
        digits = string.Empty;
        var groups = new List<StringBuilder> { new() };
        foreach (var c in integerPart)
        {
            if (SpaceLike.Contains(c) || c == groupSeparator)
            {
                groups.Add(new StringBuilder());
            }
            else if (IsDigit(c))
            {
                groups[^1].Append(c);
            }
            else
            {
                return false;
            }
        }

        if (groups.Count == 1)
        {
            digits = groups[0].ToString();
            return true;
        }

        for (var i = 0; i < groups.Count; i++)
        {
            var length = groups[i].Length;
            var validLength = i == 0 ? length is >= 1 and <= 3 : length == 3;
            if (!validLength)
            {
                return false;
            }
        }

        if (groups[0][0] == '0')
        {
            return false;
        }

        digits = string.Concat(groups);
        return true;
    }

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    [GeneratedRegex(@"€|\bEUR\b|\bEURO\b", RegexOptions.IgnoreCase)]
    private static partial Regex CurrencyMarks();

    // ---------- dates ----------

    private static readonly string[] IsoAndDottedFormats =
        ["yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "d.M.yy"];

    private static readonly string[] DayFirstNumericFormats =
        ["dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy"];

    private static readonly string[] MonthFirstNumericFormats =
        ["MM/dd/yyyy", "M/d/yyyy", "MM-dd-yyyy", "M-d-yyyy"];

    private static readonly string[] WordFormats =
        ["d MMM yyyy", "d MMMM yyyy", "MMM d, yyyy", "MMMM d, yyyy", "d. MMMM yyyy", "d. MMM yyyy", "d MMM. yyyy"];

    /// <summary>
    /// Parses a printed date. Numeric day/month order for "03/04/2026" follows the supplier's culture;
    /// month names are tried in the culture's own language and then in English. Impossible dates and years before 1900 are refused.
    /// </summary>
    public static bool TryParseDate(string? text, CultureInfo culture, out DateOnly value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var pattern = culture.DateTimeFormat.ShortDatePattern;
        var dayFirst = pattern.IndexOf('d', StringComparison.Ordinal) < pattern.IndexOf('M', StringComparison.Ordinal);
        var formats = IsoAndDottedFormats
            .Concat(dayFirst ? DayFirstNumericFormats : MonthFirstNumericFormats)
            .Concat(WordFormats);

        foreach (var format in formats)
        {
            foreach (var formatCulture in new[] { culture, CultureInfo.InvariantCulture })
            {
                // One format at a time: "dd.MM.yyyy" would read "02.04.26" as the year 26, which is not a hit.
                if (DateOnly.TryParseExact(text.Trim(), format, formatCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed) && parsed.Year >= 1900)
                {
                    value = parsed;
                    return true;
                }
            }
        }

        return false;
    }

    // ---------- identifiers ----------

    /// <summary>
    /// Estonian VAT number: "EE" and nine digits. Fixes spacing, case and a missing prefix, and drops labels such as
    /// "KMKR nr". Foreign VAT numbers are refused. Format only: there is no public check digit to verify.
    /// </summary>
    public static bool TryNormaliseVatNumber(string? text, out string vatNumber)
    {
        vatNumber = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var compact = Separators().Replace(VatLabels().Replace(text.ToUpperInvariant(), " "), string.Empty);
        var match = EstonianVat().Match(compact);
        if (!match.Success)
        {
            return false;
        }

        vatNumber = "EE" + match.Groups["digits"].Value;
        return true;
    }

    /// <summary>Estonian registry code: exactly eight digits, after any label such as "Reg." or "Registrikood".</summary>
    public static bool TryNormaliseRegistryCode(string? text, out string registryCode)
    {
        registryCode = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var compact = Separators().Replace(RegistryLabels().Replace(text.ToUpperInvariant(), " "), string.Empty);
        if (!EightDigits().IsMatch(compact))
        {
            return false;
        }

        registryCode = compact;
        return true;
    }

    [GeneratedRegex(@"\b(?:KMKR|KMKNR|VAT|TAX|NO|NR|NUMBER)\b")]
    private static partial Regex VatLabels();

    [GeneratedRegex(@"\b(?:REGISTRIKOOD|REG|CODE|KOOD|NO|NR)\b")]
    private static partial Regex RegistryLabels();

    [GeneratedRegex(@"[\s.\-/:]")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^(?:EE)?(?<digits>\d{9})$")]
    private static partial Regex EstonianVat();

    [GeneratedRegex(@"^\d{8}$")]
    private static partial Regex EightDigits();

    // ---------- IBAN ----------

    /// <summary>
    /// Looks for an IBAN in the text (labels like "IBAN:" are ignored) and checks its shape and its mod-97 check digits.
    /// A wrong checksum means a probable misread, not a changed bank account; the rules treat the two very differently.
    /// </summary>
    public static IbanCheck CheckIban(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new IbanCheck(IbanStatus.Malformed, null);
        }

        var match = IbanShape().Match(text.ToUpperInvariant());
        if (!match.Success)
        {
            return new IbanCheck(IbanStatus.Malformed, null);
        }

        var iban = string.Concat(match.Value.Where(c => !char.IsWhiteSpace(c)));

        // An Estonian IBAN is EE, two check digits and sixteen digits. Other countries: only the general length rule.
        var shapeOk = iban.StartsWith("EE", StringComparison.Ordinal)
            ? iban.Length == 20 && iban[2..].All(IsDigit)
            : iban.Length is >= 15 and <= 34;

        if (!shapeOk)
        {
            return new IbanCheck(IbanStatus.Malformed, iban);
        }

        return new IbanCheck(HasValidChecksum(iban) ? IbanStatus.Valid : IbanStatus.InvalidChecksum, iban);
    }

    /// <summary>ISO 13616: move the first four characters to the end, turn letters into numbers (A=10 ... Z=35), the remainder mod 97 must be 1.</summary>
    private static bool HasValidChecksum(string iban)
    {
        var remainder = 0;
        foreach (var c in iban[4..] + iban[..4])
        {
            var number = IsDigit(c) ? c - '0' : c - 'A' + 10;
            remainder = (number < 10 ? remainder * 10 + number : remainder * 100 + number) % 97;
        }

        return remainder == 1;
    }

    // Two letters, two digits, groups of four, a short last group. Spaces between groups are allowed.
    [GeneratedRegex(@"[A-Z]{2}\d{2}(?:\s?[A-Z0-9]{4}){2,7}(?:\s?[A-Z0-9]{1,3})?")]
    private static partial Regex IbanShape();

    // ---------- units ----------

    private static readonly Dictionary<string, UnitCode> UnitSpellings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["m3"] = UnitCode.CubicMetre, ["m³"] = UnitCode.CubicMetre, ["tm"] = UnitCode.CubicMetre, ["tihumeeter"] = UnitCode.CubicMetre,
        ["rm"] = UnitCode.StackedCubicMetre, ["ruumimeeter"] = UnitCode.StackedCubicMetre,
        ["jm"] = UnitCode.RunningMetre, ["jooksev meeter"] = UnitCode.RunningMetre, ["running metre"] = UnitCode.RunningMetre,
        ["m2"] = UnitCode.SquareMetre, ["m²"] = UnitCode.SquareMetre,
        ["tk"] = UnitCode.Piece, ["pcs"] = UnitCode.Piece, ["pc"] = UnitCode.Piece, ["pieces"] = UnitCode.Piece, ["tükk"] = UnitCode.Piece,
        ["l"] = UnitCode.Litre, ["ltr"] = UnitCode.Litre, ["liter"] = UnitCode.Litre, ["litre"] = UnitCode.Litre,
        ["kg"] = UnitCode.Kilogram,
        ["km"] = UnitCode.Kilometre,
        ["reis"] = UnitCode.Trip, ["trip"] = UnitCode.Trip,
        ["karp"] = UnitCode.Box, ["box"] = UnitCode.Box,
        ["pakk"] = UnitCode.Pack, ["pack"] = UnitCode.Pack,
        ["pall"] = UnitCode.Pallet, ["pallet"] = UnitCode.Pallet,
        ["purk"] = UnitCode.Can, ["can"] = UnitCode.Can,
        ["rull"] = UnitCode.Roll, ["roll"] = UnitCode.Roll,
    };

    /// <summary>
    /// Maps the spelling on the document to a unit code. A plain dictionary for now; conversion between units
    /// (a pack into cubic metres) comes from the supplier's item data, never from here.
    /// </summary>
    public static bool TryNormaliseUnit(string? text, out UnitCode unit)
    {
        unit = UnitCode.Unknown;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var key = string.Join(' ', text.Trim().TrimEnd('.').Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return UnitSpellings.TryGetValue(key, out unit);
    }
}
