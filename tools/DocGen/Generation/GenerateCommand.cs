using System.Globalization;
using System.Text;
using System.Text.Json;
using DocGen.Seeding;
using QuestPDF.Infrastructure;

namespace DocGen.Generation;

internal static class GenerateCommand
{
    public const int PlanSeed = 20261011;

    public static int Run(string[] args)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var outputDirectory = ValueOf(args, "--out") ?? Path.Combine("data", "generated");
        var dryRun = args.Contains("--dry-run");

        // The same seeded data the `seed` command writes to Azure SQL, so no database is needed here.
        var erp = new ErpDataBuilder(ErpDataBuilder.DefaultSeed).Build();
        var documents = new DocumentPlanner(erp, PlanSeed).Plan();
        PrintSummary(documents);

        if (dryRun)
        {
            Console.WriteLine("Dry run: no files written.");
            return 0;
        }

        Directory.CreateDirectory(Path.Combine(outputDirectory, "invoices"));
        Directory.CreateDirectory(Path.Combine(outputDirectory, "delivery-notes"));
        Directory.CreateDirectory(Path.Combine(outputDirectory, "ground_truth"));

        var rendered = new Dictionary<PlannedDocument, byte[]>();
        foreach (var doc in documents.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            // An exact resend is the same file again, byte for byte, so it has the same content hash.
            var bytes = doc.ExactCopyOf is { } original
                ? rendered[original]
                : doc.Kind == DocumentKind.Invoice ? PdfRenderer.RenderInvoice(doc) : PdfRenderer.RenderDeliveryNote(doc);
            rendered[doc] = bytes;

            File.WriteAllBytes(Path.Combine(outputDirectory, doc.FileName), bytes);

            var truth = GroundTruth.From(doc, doc.ExactCopyOf?.Id ?? string.Empty);
            File.WriteAllText(Path.Combine(outputDirectory, "ground_truth", $"{doc.Id}.json"),
                JsonSerializer.Serialize(truth, GroundTruth.JsonOptions) + "\n");
        }

        WriteManifest(Path.Combine(outputDirectory, "manifest.csv"), documents);
        Console.WriteLine($"Wrote {documents.Count} documents to {Path.GetFullPath(outputDirectory)}");
        return 0;
    }

    private static string? ValueOf(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void WriteManifest(string path, IReadOnlyList<PlannedDocument> documents)
    {
        var builder = new StringBuilder();
        builder.AppendLine("document_id,document_type,file,supplier,layout,language,split,quality,injected_codes,expected_decision,po_number,goods_receipt,duplicate_of");

        foreach (var doc in documents.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            var fields = new[]
            {
                doc.Id,
                doc.Kind == DocumentKind.Invoice ? "Invoice" : "DeliveryNote",
                doc.FileName,
                doc.Supplier.Name,
                doc.Layout.Id,
                doc.Layout.Culture[..2],
                doc.Split,
                doc.Quality,
                string.Join(';', doc.Injected.Select(d => GroundTruth.CodeName(d.Code))),
                doc.ExpectedDecision.ToString(),
                doc.PurchaseOrderNumber,
                doc.GoodsReceiptNumber ?? string.Empty,
                doc.ExactCopyOf?.Id ?? string.Empty,
            };
            builder.AppendLine(string.Join(',', fields.Select(Csv)));
        }

        File.WriteAllText(path, builder.ToString());
    }

    private static string Csv(string value) =>
        value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;

    private static void PrintSummary(IReadOnlyList<PlannedDocument> documents)
    {
        var invoices = documents.Where(d => d.Kind == DocumentKind.Invoice).ToList();
        var notes = documents.Where(d => d.Kind == DocumentKind.DeliveryNote).ToList();
        var injectedInvoices = invoices.Count(d => d.Injected.Count > 0);

        Console.WriteLine($"Invoices:        {invoices.Count} ({injectedInvoices} with injected findings, {Pct(injectedInvoices, invoices.Count)})");
        Console.WriteLine($"  with two codes: {invoices.Count(d => d.Injected.Count > 1)}");
        Console.WriteLine($"Delivery notes:  {notes.Count} ({notes.Count(d => d.Injected.Count > 0)} with a quantity mismatch)");
        Console.WriteLine($"Test split:      {documents.Count(d => d.Split == "test")} of {documents.Count} ({Pct(documents.Count(d => d.Split == "test"), documents.Count)})");
        Console.WriteLine();

        Console.WriteLine("Findings by code (dev / test):");
        foreach (var group in documents.SelectMany(d => d.Injected.Select(i => (d.Split, i.Code))).GroupBy(x => x.Code).OrderBy(g => g.Key))
        {
            Console.WriteLine($"  {GroundTruth.CodeName(group.Key),-22} {group.Count(x => x.Split == "dev"),3} / {group.Count(x => x.Split == "test"),3}");
        }

        Console.WriteLine();
        Console.WriteLine("Documents by layout (dev / test):");
        foreach (var group in documents.GroupBy(d => d.Layout.Id).OrderBy(g => g.Key))
        {
            Console.WriteLine($"  {group.Key}{(group.First().Layout.HeldOut ? " (held out)" : string.Empty),-12} {group.Count(d => d.Split == "dev"),3} / {group.Count(d => d.Split == "test"),3}");
        }

        Console.WriteLine();
        Console.WriteLine("Expected decisions: " + string.Join(", ", documents.GroupBy(d => d.ExpectedDecision).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}")));
    }

    private static string Pct(int part, int whole) => (100.0 * part / whole).ToString("0", CultureInfo.InvariantCulture) + " %";
}
