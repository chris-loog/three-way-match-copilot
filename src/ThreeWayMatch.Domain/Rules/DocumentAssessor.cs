using System.Globalization;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Domain.Matching;
using ThreeWayMatch.Domain.Normalisation;

namespace ThreeWayMatch.Domain.Rules;

/// <summary>
/// The whole judgement for one document: match it, double-check what was read, run every rule, decide.
/// It never approves anything itself beyond saying "AutoApproved"; the pipeline acts on the decision.
/// </summary>
public sealed class DocumentAssessor(IReadOnlyList<IDiscrepancyRule> rules)
{
    public static DocumentAssessor CreateDefault() => new(DefaultRules.Create());

    public Assessment AssessInvoice(
        ExtractedInvoice invoice, string? contentHash, MatchingSnapshot snapshot,
        IReadOnlyList<ProcessedInvoice> processed, DiscrepancyOptions? options = null)
    {
        options ??= new DiscrepancyOptions();

        // Match first (it finds the supplier), then cross-check the extraction with the supplier's own number and date format.
        var match = MatchingEngine.Match(MatchInput.FromInvoice(invoice), snapshot);
        var culture = CultureOf(match.Supplier.Supplier?.DocumentCulture);
        var checkedInvoice = ExtractionCrossCheck.Apply(invoice, culture);

        return Assess(new RuleContext
        {
            Kind = DocumentType.Invoice, Match = match, Snapshot = snapshot, Options = options,
            Invoice = checkedInvoice, ContentHash = contentHash, ProcessedInvoices = processed,
        });
    }

    public Assessment AssessDeliveryNote(
        MatchInput note, string? deliveryNoteNumber, IReadOnlyList<string> lowConfidenceFields,
        MatchingSnapshot snapshot, DiscrepancyOptions? options = null) =>
        Assess(new RuleContext
        {
            Kind = DocumentType.DeliveryNote, Match = MatchingEngine.Match(note, snapshot), Snapshot = snapshot,
            Options = options ?? new DiscrepancyOptions(), DeliveryNoteNumber = deliveryNoteNumber, LowConfidenceFields = lowConfidenceFields,
        });

    public Assessment Assess(RuleContext context)
    {
        var findings = rules.SelectMany(rule => rule.Evaluate(context)).ToList();
        var concerns = context.Match.Concerns;
        return new Assessment(DecisionMaker.Decide(findings, concerns), findings, concerns, context.Match, DecisionMaker.AmountAtStake(findings))
        {
            CheckedInvoice = context.Invoice,
        };
    }

    private static CultureInfo CultureOf(string? name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name ?? "et-EE");
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo("et-EE");
        }
    }
}
