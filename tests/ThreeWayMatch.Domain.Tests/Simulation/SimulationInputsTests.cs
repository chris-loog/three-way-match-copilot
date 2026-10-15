using DocGen.Evaluation;
using DocGen.Generation;
using DocGen.Seeding;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Extraction;

namespace ThreeWayMatch.Domain.Tests.Simulation;

/// <summary>The hooks the evaluation run uses to replace the perfect reading of chosen documents.</summary>
public class SimulationInputsTests
{
    private static ErpDataHolder Build()
    {
        var erp = new ErpDataBuilder(ErpDataBuilder.DefaultSeed).Build();
        return new ErpDataHolder(erp, new DocumentPlanner(erp, GenerateCommand.PlanSeed).Plan());
    }

    private sealed record ErpDataHolder(ErpData Erp, IReadOnlyList<PlannedDocument> Documents);

    [Fact]
    public void A_skipped_document_is_left_out_like_a_document_that_failed_to_read()
    {
        var world = Build();
        var skipped = world.Documents.First(d => d.Kind == DocumentKind.Invoice).Id;

        var results = RuleSimulation.Run(world.Erp, world.Documents, inputs: new SimulationInputs { Skip = new HashSet<string> { skipped } });

        Assert.DoesNotContain(results, r => r.Document.Id == skipped);
        Assert.Equal(world.Documents.Count - 1, results.Count);
    }

    [Fact]
    public void A_reader_that_returns_an_unsure_reading_sends_the_document_to_review()
    {
        var world = Build();
        var target = world.Documents.First(d => d.Kind == DocumentKind.Invoice && d.ExpectedDecision == Decision.AutoApproved && d.Injected.Count == 0);
        var smudged = new SimulationInputs
        {
            Invoice = d => d.Id == target.Id
                ? new ExtractedInvoice
                {
                    ModelId = "test", RawJson = "{}",
                    SupplierVatNumber = new ExtractedField<string>(target.Supplier.VatNumber, target.Supplier.VatNumber, 0.4),
                    InvoiceNumber = new ExtractedField<string>(target.Invoice!.InvoiceNumber, target.Invoice.InvoiceNumber, 0.95),
                }
                : null,
        };

        var results = RuleSimulation.Run(world.Erp, world.Documents, inputs: smudged);

        var decision = results.Single(r => r.Document.Id == target.Id).Assessment.Decision;
        Assert.NotEqual(Decision.AutoApproved, decision);
    }

    [Fact]
    public void Two_files_with_the_same_hash_are_an_exact_duplicate_even_when_the_generator_did_not_plan_one()
    {
        var world = Build();
        var invoices = world.Documents.Where(d => d.Kind == DocumentKind.Invoice && d.Injected.Count == 0 && d.ExactCopyOf is null).Take(2).ToList();
        var inputs = new SimulationInputs { Hash = d => invoices.Any(i => i.Id == d.Id) ? "same-file" : null };

        var results = RuleSimulation.Run(world.Erp, world.Documents, inputs: inputs);

        var later = invoices.OrderBy(i => i.Id, StringComparer.Ordinal).Last();
        Assert.Equal(Decision.Rejected, results.Single(r => r.Document.Id == later.Id).Assessment.Decision);
    }
}
