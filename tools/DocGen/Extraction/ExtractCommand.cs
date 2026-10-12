using System.Globalization;
using System.Text.Json;
using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Domain.Normalisation;
using ThreeWayMatch.Domain.Rules;
using ThreeWayMatch.Infrastructure.DocumentIntelligence;

namespace DocGen.Extraction;

/// <summary>
/// Sends dataset invoices to Document Intelligence and compares a few key fields with the ground truth.
/// Results are cached by content hash in data/di-cache, so running it again costs nothing for documents already read.
/// This is a quick look, not the evaluation: the real numbers come from step 8.2.
/// </summary>
internal static class ExtractCommand
{
    public const string EndpointVariable = "THREEWAYMATCH_DI_ENDPOINT";

    public static async Task<int> RunAsync(string[] args)
    {
        var endpoint = Environment.GetEnvironmentVariable(EndpointVariable);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            Console.Error.WriteLine($"Set {EndpointVariable}, e.g. https://di-three-way-match.cognitiveservices.azure.com/");
            return 1;
        }

        var dataset = ValueOf(args, "--dataset") ?? Path.Combine("data", "dataset");
        var split = ValueOf(args, "--split") ?? "dev";
        var quality = ValueOf(args, "--quality");
        var limit = int.Parse(ValueOf(args, "--limit") ?? "10", CultureInfo.InvariantCulture);
        var cacheDirectory = ValueOf(args, "--cache") ?? Path.Combine("data", "di-cache");
        var threshold = new DiscrepancyOptions().ConfidenceThreshold;

        var rows = ReadManifest(Path.Combine(dataset, "manifest.csv"))
            .Where(r => r["document_type"] == "Invoice" && string.IsNullOrEmpty(r["duplicate_of"]))
            .Where(r => split == "all" || r["split"] == split)
            .Where(r => quality is null || r["quality"] == quality)
            .Take(limit)
            .ToList();

        Console.WriteLine($"Reading {rows.Count} invoices (split={split}, quality={quality ?? "any"}). Free tier: 1 page each, one call every 3 s.");
        var extractor = DocumentIntelligenceRegistration.Create(new DocumentIntelligenceOptions
        {
            Endpoint = new Uri(endpoint),
            CacheDirectory = cacheDirectory,
        });

        var correct = new Dictionary<string, int>();
        foreach (var row in rows)
        {
            await using var stream = File.OpenRead(Path.Combine(dataset, row["file"]));
            // Our own reading of the printed text must agree with the model's; where it does not, confidence drops to 0.
            var culture = CultureInfo.GetCultureInfo(row["language"] == "et" ? "et-EE" : "en-GB");
            var invoice = ExtractionCrossCheck.Apply(await extractor.ExtractInvoiceAsync(stream), culture);
            var truth = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(dataset, "ground_truth", $"{row["document_id"]}.json")));
            var header = truth.RootElement.GetProperty("header");

            var checks = new (string Name, bool Ok)[]
            {
                ("invoice_no", Same(invoice.InvoiceNumber.Value, header.GetProperty("invoice_number").GetString())),
                ("po_ref", Same(invoice.PurchaseOrderReference.Value, StringOrNull(header, "po_reference"))),
                ("vat_no", Same(invoice.SupplierVatNumber.Value, truth.RootElement.GetProperty("supplier_vat_number").GetString())),
                ("gross", invoice.GrossTotal.Value == header.GetProperty("gross_total").GetDecimal()),
                ("iban", Same(invoice.Iban.Value, header.GetProperty("iban").GetString())),
                ("lines", invoice.Lines.Count == truth.RootElement.GetProperty("lines").GetArrayLength()),
            };

            foreach (var (name, ok) in checks)
            {
                correct[name] = correct.GetValueOrDefault(name) + (ok ? 1 : 0);
            }

            var low = invoice.LowConfidenceFields(threshold);
            Console.WriteLine($"{row["document_id"]} {row["quality"],-10} {row["layout"]} min={invoice.MinKeyFieldConfidence():0.00} " +
                $"{string.Join(' ', checks.Select(c => c.Ok ? c.Name : c.Name.ToUpperInvariant() + "!"))}" +
                (low.Count > 0 ? $"  low: {string.Join(',', low.Take(3))}" : string.Empty));
        }

        Console.WriteLine();
        Console.WriteLine("Correct: " + string.Join(", ", correct.Select(c => $"{c.Key} {c.Value}/{rows.Count}")));
        return 0;
    }

    /// <summary>Spaces and case do not count here: normalisation (step 3.2) handles them properly.</summary>
    private static bool Same(string? extracted, string? truth) =>
        string.Equals(Strip(extracted), Strip(truth), StringComparison.OrdinalIgnoreCase);

    private static string Strip(string? value) => (value ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal);

    private static string? StringOrNull(JsonElement element, string name) =>
        element.GetProperty(name).ValueKind == JsonValueKind.Null ? null : element.GetProperty(name).GetString();

    private static string? ValueOf(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static List<Dictionary<string, string>> ReadManifest(string path)
    {
        var lines = File.ReadAllLines(path);
        var header = SplitCsv(lines[0]);
        return lines.Skip(1).Where(l => l.Length > 0)
            .Select(l => SplitCsv(l).Select((value, i) => (header[i], value)).ToDictionary(p => p.Item1, p => p.value))
            .ToList();
    }

    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted && c == '"' && i + 1 < line.Length && line[i + 1] == '"')
            {
                current.Append('"');
                i++;
            }
            else if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
