using ThreeWayMatch.Domain.Ap;

namespace ThreeWayMatch.Domain.Rules.Rules;

/// <summary>PARTIAL_DELIVERY: the invoice arrived before the goods. Not an error: the document waits (OnHold) until the receipt is booked.</summary>
public sealed class PartialDeliveryRule : IDiscrepancyRule
{
    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        if (context.Kind != DocumentType.Invoice)
        {
            yield break;
        }

        foreach (var issue in QuantityAnalysis.Find(context).Where(i => i.Kind == QuantityIssueKind.PartialDelivery))
        {
            yield return new Finding(DiscrepancyCode.PartialDelivery, Severity.Warning,
                issue.LineNumber, Tolerances.Text(issue.OpenOnReceipts), Tolerances.Text(issue.Invoiced), issue.Amount);
        }
    }
}
