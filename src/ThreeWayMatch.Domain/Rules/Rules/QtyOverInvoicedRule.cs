using ThreeWayMatch.Domain.Ap;

namespace ThreeWayMatch.Domain.Rules.Rules;

/// <summary>QTY_OVER_INVOICED: invoiced more than has been received and not yet invoiced (plus tolerance), with no goods still expected.</summary>
public sealed class QtyOverInvoicedRule : IDiscrepancyRule
{
    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        if (context.Kind != DocumentType.Invoice)
        {
            yield break;
        }

        foreach (var issue in QuantityAnalysis.Find(context).Where(i => i.Kind == QuantityIssueKind.OverInvoiced))
        {
            yield return new Finding(DiscrepancyCode.QtyOverInvoiced, Tolerances.Overpayment(issue.Amount, context.Options),
                issue.LineNumber, Tolerances.Text(issue.OpenOnReceipts), Tolerances.Text(issue.Invoiced), issue.Amount);
        }
    }
}
