using System.Globalization;
using System.Text;

namespace DocGen.Evaluation;

/// <summary>Writes rows of values as CSV: quotes anything with a comma, quote or line break, and writes numbers with a point.</summary>
internal static class EvalCsv
{
    public static void Write(string path, IReadOnlyList<string> columns, IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        var builder = new StringBuilder(string.Join(',', columns)).Append('\n');
        foreach (var row in rows)
        {
            builder.Append(string.Join(',', columns.Select(c => Cell(row.GetValueOrDefault(c))))).Append('\n');
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, builder.ToString());
    }

    private static string Cell(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            bool b => b ? "1" : "0",
            double d => d.ToString("0.####", CultureInfo.InvariantCulture),
            decimal m => m.ToString("0.####", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

        return text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;
    }
}
