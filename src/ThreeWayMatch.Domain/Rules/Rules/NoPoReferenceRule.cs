using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Matching;

namespace ThreeWayMatch.Domain.Rules.Rules;

/// <summary>
/// NO_PO_REFERENCE: the PO number is missing, unknown, or belongs to another supplier. It is reported even when the
/// engine could suggest an order from the lines: the suggestion lets the other rules run, but the invoice is still not
/// properly referenced and a person must confirm it.
/// </summary>
public sealed class NoPoReferenceRule : IDiscrepancyRule
{
    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        if (context.Kind != DocumentType.Invoice || context.Match.PurchaseOrder.ReferenceStatus == PurchaseOrderReferenceStatus.Matched)
        {
            yield break;
        }

        var printed = context.Match.PurchaseOrder.PrintedReference;
        yield return new Finding(DiscrepancyCode.NoPoReference, Severity.Warning, null,
            context.Match.PurchaseOrder.PurchaseOrder?.Number ?? "a known purchase order of the supplier",
            string.IsNullOrWhiteSpace(printed) ? "(blank)" : printed,
            context.Invoice?.NetTotal.Value ?? 0m);
    }
}
