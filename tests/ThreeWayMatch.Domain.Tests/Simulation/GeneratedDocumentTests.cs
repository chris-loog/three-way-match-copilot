using DocGen.Evaluation;
using DocGen.Generation;
using DocGen.Seeding;
using ThreeWayMatch.Domain.Ap;

namespace ThreeWayMatch.Domain.Tests.Simulation;

/// <summary>
/// The whole judgement (match, cross-check, every rule, decision) against every generated document, with the extraction
/// assumed perfect. Reading the page is a separate question, measured in step 8.2. These tests are the main KPI as code:
/// nothing with a real injected problem is ever approved automatically.
/// </summary>
public class GeneratedDocumentTests
{
    private static readonly Lazy<IReadOnlyList<SimulatedResult>> Results = new(() =>
    {
        var erp = new ErpDataBuilder(ErpDataBuilder.DefaultSeed).Build();
        var documents = new DocumentPlanner(erp, GenerateCommand.PlanSeed).Plan();
        return RuleSimulation.Run(erp, documents);
    });

    private static string Ids(IEnumerable<SimulatedResult> results) => string.Join(", ", results.Select(r => r.Document.Id));

    [Fact]
    public void The_simulation_is_not_vacuous()
    {
        Assert.True(Results.Value.Count >= 250);
        Assert.True(Results.Value.Count(r => r.Document.Injected.Count > 0) >= 60);
        Assert.Contains(Results.Value, r => r.Document.Kind == DocumentKind.DeliveryNote);
    }

    [Fact]
    public void Every_discrepancy_in_the_catalogue_that_the_generator_injects_is_exercised()
    {
        var injected = Results.Value.SelectMany(r => r.Document.Injected.Select(i => i.Code)).ToHashSet();
        var catalogue = Enum.GetValues<DiscrepancyCode>().Where(c => c != DiscrepancyCode.LowConfidence); // comes from the scan simulation, not from here

        var notExercised = catalogue.Where(c => !injected.Contains(c)).ToList();

        Assert.True(notExercised.Count == 0, $"Never injected: {string.Join(", ", notExercised)}");
    }

    [Fact]
    public void No_document_with_a_real_injected_problem_is_ever_approved_automatically()
    {
        var wrong = Results.Value.Where(CheckRulesCommand.IsWrongAutoApproval).ToList();

        Assert.True(wrong.Count == 0, $"Wrongly auto-approved: {Ids(wrong)}");
    }

    [Fact]
    public void Every_injected_finding_is_caught_by_its_rule()
    {
        var missed = Results.Value
            .SelectMany(r => r.Document.Injected.Where(i => r.Assessment.Findings.All(f => f.Code != i.Code)).Select(i => $"{r.Document.Id}:{i.Code}"))
            .ToList();

        Assert.True(missed.Count == 0, $"Not caught: {string.Join(", ", missed)}");
    }

    [Fact]
    public void Clean_documents_are_never_sent_to_review_unnecessarily()
    {
        var falseAlarms = Results.Value.Where(r => r.Document.Injected.Count == 0 && r.Assessment.Decision != Decision.AutoApproved).ToList();

        Assert.True(falseAlarms.Count == 0, $"False alarms: {Ids(falseAlarms)}");
    }

    [Fact]
    public void Every_document_gets_the_decision_the_generator_expects()
    {
        var different = Results.Value.Where(r => r.Assessment.Decision != r.Document.ExpectedDecision)
            .Select(r => $"{r.Document.Id}: expected {r.Document.ExpectedDecision}, got {r.Assessment.Decision}").ToList();

        Assert.True(different.Count == 0, string.Join("; ", different));
    }

    [Fact]
    public void Overpayments_above_the_threshold_are_blocks()
    {
        var overpayments = Results.Value.SelectMany(r => r.Assessment.Findings)
            .Where(f => f.Code is DiscrepancyCode.PriceAbovePo or DiscrepancyCode.QtyOverInvoiced).ToList();

        Assert.All(overpayments.Where(f => f.AmountAtStake > 500m), f => Assert.Equal(Severity.Block, f.Severity));
        Assert.Contains(overpayments, f => f.Severity == Severity.Block); // the data really contains one
    }

    [Fact]
    public void The_review_queue_can_be_sorted_by_money_at_risk()
    {
        var queue = Results.Value.Where(r => r.Assessment.Decision == Decision.NeedsReview).OrderByDescending(r => r.Assessment.AmountAtStake).ToList();

        Assert.NotEmpty(queue);
        Assert.True(queue[0].Assessment.AmountAtStake > queue[^1].Assessment.AmountAtStake);
    }
}
