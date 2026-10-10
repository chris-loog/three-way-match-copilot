using DocGen.Generation;
using DocGen.Seeding;
using ThreeWayMatch.Domain.Ap;

namespace DocGen.Evaluation;

/// <summary>
/// Offline check of the matching engine and rules against the generator's ground truth (no Azure, no database).
/// Exit code 1 if anything with an injected problem was approved automatically.
/// </summary>
internal static class CheckRulesCommand
{
    public static int Run(string[] args)
    {
        var erp = new ErpDataBuilder(ErpDataBuilder.DefaultSeed).Build();
        var documents = new DocumentPlanner(erp, GenerateCommand.PlanSeed).Plan();
        var results = RuleSimulation.Run(erp, documents);
        var verbose = args.Contains("--verbose");

        var wrongApprovals = results.Where(IsWrongAutoApproval).ToList();
        var wrongDecisions = results.Where(r => r.Assessment.Decision != r.Document.ExpectedDecision).ToList();
        var missed = results.SelectMany(r => r.Document.Injected.Where(i => !r.Assessment.Findings.Any(f => f.Code == i.Code)).Select(i => (r, i))).ToList();
        var unexpected = results.SelectMany(r => r.Assessment.Findings
            .Where(f => f.Severity >= Severity.Warning || f.Code == DiscrepancyCode.PriceBelowPo)
            .Where(f => !r.Document.Injected.Any(i => i.Code == f.Code))
            .Select(f => (r, f))).ToList();

        Console.WriteLine($"Documents: {results.Count}");
        Console.WriteLine($"Wrong automatic approvals: {wrongApprovals.Count} of {results.Count(r => r.Document.Injected.Any(i => i.Severity >= Severity.Warning))} documents with a real injected problem");
        Console.WriteLine($"Decision differs from expectation: {wrongDecisions.Count}");
        Console.WriteLine($"Injected findings not caught: {missed.Count} of {results.Sum(r => r.Document.Injected.Count)}");
        Console.WriteLine($"Findings that were not injected: {unexpected.Count}");
        Console.WriteLine();

        Console.WriteLine("Decision (rows: expected, columns: actual)");
        var decisions = Enum.GetValues<Decision>();
        Console.WriteLine($"{string.Empty,-14}" + string.Concat(decisions.Select(d => $"{d,-13}")));
        foreach (var expected in decisions)
        {
            Console.WriteLine($"{expected,-14}" + string.Concat(decisions.Select(actual =>
                $"{results.Count(r => r.Document.ExpectedDecision == expected && r.Assessment.Decision == actual),-13}")));
        }

        Console.WriteLine();
        Console.WriteLine("Recall per injected code");
        foreach (var group in results.SelectMany(r => r.Document.Injected.Select(i => (r, i))).GroupBy(x => x.i.Code).OrderBy(g => g.Key.ToString()))
        {
            var caught = group.Count(x => x.r.Assessment.Findings.Any(f => f.Code == x.i.Code));
            Console.WriteLine($"  {GroundTruth.CodeName(group.Key),-24} {caught,3} / {group.Count(),3}");
        }

        void Show(string title, IEnumerable<string> lines)
        {
            var list = lines.ToList();
            if (list.Count == 0 || !verbose && list.Count > 12)
            {
                if (list.Count > 0)
                {
                    Console.WriteLine($"\n{title} ({list.Count}); first 12:");
                    list = list.Take(12).ToList();
                }
                else
                {
                    return;
                }
            }
            else
            {
                Console.WriteLine($"\n{title} ({list.Count}):");
            }

            foreach (var line in list)
            {
                Console.WriteLine("  " + line);
            }
        }

        string Codes(SimulatedResult r) => string.Join(",", r.Assessment.Findings.Select(f => $"{GroundTruth.CodeName(f.Code)}[{f.Severity}]"));
        string Injected(PlannedDocument d) => string.Join(",", d.Injected.Select(i => GroundTruth.CodeName(i.Code)));

        Show("WRONG AUTO-APPROVALS", wrongApprovals.Select(r => $"{r.Document.Id}: injected {Injected(r.Document)}; found {Codes(r)}"));
        Show("Decision differs", wrongDecisions.Select(r => $"{r.Document.Id}: expected {r.Document.ExpectedDecision}, got {r.Assessment.Decision}; injected [{Injected(r.Document)}] found [{Codes(r)}] concerns [{string.Join(";", r.Assessment.Concerns.Select(c => c.Kind))}]"));
        Show("Missed", missed.Select(x => $"{x.r.Document.Id}: {GroundTruth.CodeName(x.i.Code)} (found: {Codes(x.r)})"));
        Show("Unexpected", unexpected.Select(x => $"{x.r.Document.Id}: {GroundTruth.CodeName(x.f.Code)} expected={x.f.Expected} actual={x.f.Actual} (injected: {Injected(x.r.Document)})"));

        return wrongApprovals.Count == 0 ? 0 : 1;
    }

    /// <summary>An approval is wrong when a real problem (Warning or worse) was injected. Information-only changes may pass.</summary>
    internal static bool IsWrongAutoApproval(SimulatedResult r) =>
        r.Assessment.Decision == Decision.AutoApproved && r.Document.Injected.Any(i => i.Severity >= Severity.Warning);
}
