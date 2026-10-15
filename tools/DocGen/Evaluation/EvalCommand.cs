using System.Globalization;
using System.Text.Json;
using DocGen.Generation;
using DocGen.Seeding;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Domain.Normalisation;
using ThreeWayMatch.Domain.Rules;
using ThreeWayMatch.Infrastructure.DocumentIntelligence;

namespace DocGen.Evaluation;

/// <summary>
/// The evaluation run (step 8.2). Every document in the dataset goes through extraction (from the hash cache where possible),
/// matching and the rules, in id order, with the same code as the pipeline. Writes results.csv (one row per document),
/// lines.csv (one row per invoice line) and sweep.csv (the confidence threshold against straight-through rate and wrong approvals).
/// <para>
/// Test-split discipline: only documents of the chosen split are read by Document Intelligence. The others are read as their printed
/// text, so the shared state (what earlier invoices already billed, earlier invoice numbers) is complete without touching their files.
/// </para>
/// </summary>
internal static class EvalCommand
{
    public const string EndpointVariable = "THREEWAYMATCH_DI_ENDPOINT";

    private static readonly string[] ResultColumns =
    [
        "document_id", "kind", "split", "layout", "held_out", "quality", "supplier", "extract_model", "pages",
        "expected_decision", "decision", "correct_decision", "injected_codes", "found_codes", "min_conf", "low_fields", "amount_at_stake",
        "real_problem", "wrong_auto_approval", "false_alarm",
        "f_number", "f_po_reference", "f_supplier_vat", "f_date", "f_net", "f_vat", "f_gross", "f_iban", "f_line_count", "f_quantities",
        "n_lines", "lines_matched_ok", "error",
    ];

    public static async Task<int> RunAsync(string[] args)
    {
        var split = ValueOf(args, "--split") ?? "dev";
        var tag = ValueOf(args, "--tag") ?? "run";
        var dataset = ValueOf(args, "--dataset") ?? Path.Combine("data", "dataset");
        var cacheDirectory = ValueOf(args, "--cache") ?? Path.Combine("data", "di-cache");
        var outDirectory = ValueOf(args, "--out") ?? Path.Combine("eval", "out", tag);
        var cacheOnly = args.Contains("--cache-only");
        var threshold = double.Parse(ValueOf(args, "--threshold") ?? "0.80", CultureInfo.InvariantCulture);
        var thresholds = (ValueOf(args, "--thresholds") ?? "0.60,0.65,0.70,0.75,0.80,0.85,0.90,0.95")
            .Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToList();
        var interval = double.Parse(ValueOf(args, "--interval") ?? "3", CultureInfo.InvariantCulture);

        var endpoint = Environment.GetEnvironmentVariable(EndpointVariable);
        if (!cacheOnly && string.IsNullOrWhiteSpace(endpoint))
        {
            Console.Error.WriteLine($"Set {EndpointVariable}, or pass --cache-only to read cached results only.");
            return 1;
        }

        if (split != "dev")
        {
            Console.WriteLine("NOTE: the test split is reported once, at the end. Any later run is 'v2' and must be reported as such.");
        }

        var manifest = ReadManifest(Path.Combine(dataset, "manifest.csv"));
        var extractor = DocumentIntelligenceRegistration.Create(new DocumentIntelligenceOptions
        {
            Endpoint = new Uri(endpoint ?? "https://cache-only.invalid/"),
            CacheDirectory = cacheDirectory,
            CacheOnly = cacheOnly,
            MinimumCallInterval = TimeSpan.FromSeconds(interval),
            DeliveryNoteModelId = ValueOf(args, "--dn-model") ?? Environment.GetEnvironmentVariable("THREEWAYMATCH_DI_DELIVERY_NOTE_MODEL") ?? "prebuilt-layout",
        });

        // Read what has to be read.
        var documents = Plan().Documents;
        var hashes = new Dictionary<string, string>();
        var invoices = new Dictionary<string, ExtractedInvoice>();
        var notes = new Dictionary<string, ExtractedDeliveryNote>();
        var errors = new Dictionary<string, string>();
        var inScope = documents.Where(d => split == "all" || d.Split == split).OrderBy(d => d.Id, StringComparer.Ordinal).ToList();

        foreach (var document in documents)
        {
            hashes[document.Id] = ContentHash.Sha256Hex(await File.ReadAllBytesAsync(Path.Combine(dataset, manifest[document.Id]["file"])));
        }

        Console.WriteLine($"Reading {inScope.Count} documents (split={split}); the others are read as printed text.");
        foreach (var document in inScope)
        {
            try
            {
                await using var stream = File.OpenRead(Path.Combine(dataset, manifest[document.Id]["file"]));
                if (document.Kind == DocumentKind.Invoice)
                {
                    invoices[document.Id] = await extractor.ExtractInvoiceAsync(stream);
                }
                else
                {
                    notes[document.Id] = await extractor.ExtractDeliveryNoteAsync(stream);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors[document.Id] = ex.Message.ReplaceLineEndings(" ");
                Console.WriteLine($"  {document.Id}: {ex.Message.ReplaceLineEndings(" ")}");
            }
        }

        // The run that is reported.
        var (results, plan) = Simulate(threshold);
        WriteResults(results, plan.Documents.Where(d => errors.ContainsKey(d.Id)).ToList(), plan);

        // The sweep.
        var sweep = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var t in thresholds)
        {
            var (swept, _) = Simulate(t);
            var decisions = swept.ToDictionary(r => r.Document.Id, r => r.Assessment.Decision);

            // A document that could not be read is never approved, so it counts as a document that was not.
            bool Approved(PlannedDocument d) => decisions.GetValueOrDefault(d.Id, Decision.NeedsReview) == Decision.AutoApproved && !errors.ContainsKey(d.Id);
            var realProblems = inScope.Where(d => d.Injected.Any(i => i.Severity >= Severity.Warning)).ToList();
            var clean = inScope.Where(d => d.ExpectedDecision == Decision.AutoApproved).ToList();
            var approvedCount = inScope.Count(Approved);
            sweep.Add(new Dictionary<string, object?>
            {
                ["threshold"] = t, ["split"] = split, ["documents"] = inScope.Count,
                ["auto_approved"] = approvedCount,
                ["straight_through_rate"] = inScope.Count == 0 ? 0 : (double)approvedCount / inScope.Count,
                ["wrong_auto_approvals"] = realProblems.Count(Approved),
                ["real_problem_documents"] = realProblems.Count,
                ["false_alarms"] = clean.Count(d => !Approved(d)),
                ["clean_documents"] = clean.Count,
            });
        }

        EvalCsv.Write(Path.Combine(outDirectory, "sweep.csv"),
            ["threshold", "split", "documents", "auto_approved", "straight_through_rate", "wrong_auto_approvals", "real_problem_documents", "false_alarms", "clean_documents"], sweep);
        Console.WriteLine($"Wrote results.csv, lines.csv and sweep.csv to {Path.GetFullPath(outDirectory)}");
        return 0;

        bool InScope(PlannedDocument d) => split == "all" || d.Split == split;

        (IReadOnlyList<SimulatedResult> Results, Plan Plan) Simulate(double confidenceThreshold)
        {
            var current = Plan();
            var options = new DiscrepancyOptions { ConfidenceThreshold = confidenceThreshold };
            var simulated = RuleSimulation.Run(current.Erp, current.Documents, options, new SimulationInputs
            {
                Invoice = d => invoices.GetValueOrDefault(d.Id),
                DeliveryNote = d => notes.GetValueOrDefault(d.Id),
                Hash = d => hashes[d.Id],
                Skip = errors.Keys.ToHashSet(),
            });
            return (simulated, current);
        }

        void WriteResults(IReadOnlyList<SimulatedResult> world, IReadOnlyList<PlannedDocument> failed, Plan current)
        {
            var rows = new List<IReadOnlyDictionary<string, object?>>();
            var lineRows = new List<IReadOnlyDictionary<string, object?>>();

            foreach (var result in world.Where(r => InScope(r.Document)))
            {
                var (row, lines) = Describe(result, invoices.GetValueOrDefault(result.Document.Id), notes.GetValueOrDefault(result.Document.Id), manifest[result.Document.Id]);
                rows.Add(row);
                lineRows.AddRange(lines);
            }

            foreach (var document in failed.Where(InScope))
            {
                var entry = manifest[document.Id];
                rows.Add(new Dictionary<string, object?>
                {
                    ["document_id"] = document.Id, ["kind"] = KindName(document), ["split"] = document.Split, ["layout"] = document.Layout.Id,
                    ["held_out"] = document.Layout.HeldOut, ["quality"] = entry["quality"], ["supplier"] = document.Supplier.Name,
                    ["expected_decision"] = document.ExpectedDecision.ToString(), ["decision"] = "Failed", ["correct_decision"] = false,
                    ["injected_codes"] = Codes(document.Injected.Select(i => i.Code)), ["real_problem"] = document.Injected.Any(i => i.Severity >= Severity.Warning),
                    ["wrong_auto_approval"] = false, ["false_alarm"] = document.ExpectedDecision == Decision.AutoApproved, ["error"] = errors[document.Id],
                });
            }

            EvalCsv.Write(Path.Combine(outDirectory, "results.csv"), ResultColumns, rows.OrderBy(r => (string)r["document_id"]!, StringComparer.Ordinal));
            EvalCsv.Write(Path.Combine(outDirectory, "lines.csv"),
                ["document_id", "line", "code_ok", "quantity_ok", "unit_price_ok", "matched_po_line_ok", "min_conf"], lineRows);

            int Count(Func<IReadOnlyDictionary<string, object?>, bool> test) => rows.Count(test);
            var autoApproved = Count(r => (string?)r["decision"] == nameof(Decision.AutoApproved));
            Console.WriteLine();
            Console.WriteLine($"Documents: {rows.Count}   straight through: {autoApproved} ({(rows.Count == 0 ? 0 : 100.0 * autoApproved / rows.Count):0.0}%)");
            Console.WriteLine($"Wrong automatic approvals: {Count(r => r["wrong_auto_approval"] is true)} of {Count(r => r["real_problem"] is true)} documents with a real problem");
            Console.WriteLine($"False alarms: {Count(r => r["false_alarm"] is true)} of {Count(r => (string?)r["expected_decision"] == nameof(Decision.AutoApproved))} documents that should pass");
            Console.WriteLine($"Failed to read: {failed.Count(InScope)}");
        }

        Plan Plan()
        {
            var erp = new ErpDataBuilder(ErpDataBuilder.DefaultSeed).Build();
            return new Plan(new DocumentPlanner(erp, GenerateCommand.PlanSeed).Plan().ToList(), erp);
        }
    }

    private sealed record Plan(List<PlannedDocument> Documents, ErpData Erp);

    private static string KindName(PlannedDocument d) => d.Kind == DocumentKind.Invoice ? "invoice" : "delivery_note";

    private static string Codes(IEnumerable<DiscrepancyCode> codes) => string.Join(';', codes.Select(GroundTruth.CodeName).Distinct());

    private static (Dictionary<string, object?> Row, List<Dictionary<string, object?>> Lines) Describe(
        SimulatedResult result, ExtractedInvoice? invoice, ExtractedDeliveryNote? note, Dictionary<string, string> entry)
    {
        var document = result.Document;
        var assessment = result.Assessment;
        var realProblem = document.Injected.Any(i => i.Severity >= Severity.Warning);
        var read = invoice is not null || note is not null;
        var checkedInvoice = assessment.CheckedInvoice ?? invoice;

        var row = new Dictionary<string, object?>
        {
            ["document_id"] = document.Id, ["kind"] = KindName(document), ["split"] = document.Split, ["layout"] = document.Layout.Id,
            ["held_out"] = document.Layout.HeldOut, ["quality"] = entry["quality"], ["supplier"] = document.Supplier.Name,
            ["extract_model"] = invoice?.ModelId ?? note?.ModelId ?? "printed-text",
            ["pages"] = PageCount(invoice?.RawJson ?? note?.RawJson),
            ["expected_decision"] = document.ExpectedDecision.ToString(), ["decision"] = assessment.Decision.ToString(),
            ["correct_decision"] = assessment.Decision == document.ExpectedDecision,
            ["injected_codes"] = Codes(document.Injected.Select(i => i.Code)),
            ["found_codes"] = Codes(assessment.Findings.Select(f => f.Code)),
            ["min_conf"] = checkedInvoice?.MinKeyFieldConfidence() ?? note?.MinKeyFieldConfidence(),
            ["low_fields"] = string.Join(';', invoice is not null
                ? checkedInvoice!.LowConfidenceFields(0.80, new DiscrepancyOptions().TotalsTolerance)
                : note?.LowConfidenceFields(0.80) ?? []),
            ["amount_at_stake"] = assessment.AmountAtStake,
            ["real_problem"] = realProblem,
            ["wrong_auto_approval"] = realProblem && assessment.Decision == Decision.AutoApproved,
            ["false_alarm"] = document.ExpectedDecision == Decision.AutoApproved && assessment.Decision != Decision.AutoApproved,
        };

        var lines = new List<Dictionary<string, object?>>();
        if (invoice is not null && document.Invoice is { } truth)
        {
            var seen = checkedInvoice!;
            row["f_number"] = Same(seen.InvoiceNumber.Value, truth.InvoiceNumber);
            row["f_po_reference"] = Same(seen.PurchaseOrderReference.Value, truth.PoReference);
            row["f_supplier_vat"] = Same(seen.SupplierVatNumber.Value, truth.Supplier.VatNumber);
            row["f_date"] = seen.InvoiceDate.Value == truth.InvoiceDate;
            row["f_net"] = Close(seen.NetTotal.Value, truth.NetTotal);
            row["f_vat"] = Close(seen.VatTotal.Value, truth.VatTotal);
            row["f_gross"] = Close(seen.GrossTotal.Value, truth.GrossTotal);
            row["f_iban"] = Same(seen.Iban.Value, truth.Iban);
            row["f_line_count"] = seen.Lines.Count == truth.Lines.Count;
            row["n_lines"] = truth.Lines.Count;

            var matchedOk = 0;
            foreach (var line in truth.Lines)
            {
                var got = seen.Lines.FirstOrDefault(l => l.LineNumber == line.LineNumber);
                var outcome = assessment.Match.Lines.FirstOrDefault(o => o.Line.LineNumber == line.LineNumber);
                var matched = got is not null && outcome is not null && outcome.PurchaseOrderLine?.Id == line.PurchaseOrderLine?.Id;
                matchedOk += matched ? 1 : 0;
                lines.Add(new Dictionary<string, object?>
                {
                    ["document_id"] = document.Id, ["line"] = line.LineNumber,
                    ["code_ok"] = got is not null && Same(got.ItemCode.Value, line.Code),
                    ["quantity_ok"] = got is not null && Close(got.Quantity.Value, line.Quantity, 0.0001m),
                    ["unit_price_ok"] = got is not null && Close(got.UnitPrice.Value, line.UnitPrice, 0.0001m),
                    ["matched_po_line_ok"] = matched,
                    ["min_conf"] = got is null ? 0 : new[] { got.Quantity.Confidence, got.UnitPrice.Confidence }.Min() ?? 0,
                });
            }

            row["lines_matched_ok"] = matchedOk;
        }
        else if (note is not null && document.DeliveryNote is { } noteTruth)
        {
            row["f_number"] = Same(note.DeliveryNoteNumber.Value, noteTruth.DeliveryNoteNumber);
            row["f_po_reference"] = Same(note.PurchaseOrderReference.Value, noteTruth.PoReference);
            row["f_supplier_vat"] = Same(note.SupplierVatNumber.Value, noteTruth.Supplier.VatNumber);
            row["f_date"] = note.DeliveryDate.Value == noteTruth.DeliveryDate;
            row["f_line_count"] = note.Lines.Count == noteTruth.Lines.Count;
            row["f_quantities"] = note.Lines.Count == noteTruth.Lines.Count
                && note.Lines.Zip(noteTruth.Lines).All(p => Close(p.First.Quantity.Value, p.Second.Quantity, 0.0001m));
            row["n_lines"] = noteTruth.Lines.Count;
            row["lines_matched_ok"] = noteTruth.Lines.Count(l => assessment.Match.Lines.Any(o => o.Line.LineNumber == l.LineNumber
                && o.PurchaseOrderLine?.Id == l.GoodsReceiptLine?.PurchaseOrderLine?.Id));
        }
        else if (!read)
        {
            row["error"] = "not read: printed text used";
        }

        return (row, lines);
    }

    private static bool Same(string? extracted, string? truth) => Normaliser.ReferenceKey(extracted) == Normaliser.ReferenceKey(truth);

    private static bool Close(decimal? extracted, decimal truth, decimal tolerance = 0.01m) => extracted is { } value && Math.Abs(value - truth) <= tolerance;

    private static int? PageCount(string? rawJson)
    {
        if (rawJson is null or "{}")
        {
            return null;
        }

        using var document = JsonDocument.Parse(rawJson);
        var root = document.RootElement.TryGetProperty("analyzeResult", out var inner) ? inner : document.RootElement;
        return root.TryGetProperty("pages", out var pages) ? pages.GetArrayLength() : null;
    }

    private static string? ValueOf(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static Dictionary<string, Dictionary<string, string>> ReadManifest(string path)
    {
        var lines = File.ReadAllLines(path);
        var header = lines[0].Split(',');
        // The manifest has no quoted commas in the columns used here except supplier names, which never contain one.
        return lines.Skip(1).Where(l => l.Length > 0)
            .Select(l => l.Split(','))
            .ToDictionary(f => f[0], f => header.Zip(f).ToDictionary(p => p.First, p => p.Second));
    }
}
