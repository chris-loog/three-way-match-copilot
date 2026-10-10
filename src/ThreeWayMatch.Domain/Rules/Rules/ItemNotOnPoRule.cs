using ThreeWayMatch.Domain.Ap;

namespace ThreeWayMatch.Domain.Rules.Rules;

/// <summary>
/// ITEM_NOT_ON_PO: an invoice line that cannot be matched to any line of the purchase order: an unknown product, a product
/// the order does not contain, or a charge the order has no line for. Only meaningful when the order itself is known.
/// </summary>
public sealed class ItemNotOnPoRule : IDiscrepancyRule
{
    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        if (context.Kind != DocumentType.Invoice || context.Match.PurchaseOrder.PurchaseOrder is null)
        {
            yield break;
        }

        foreach (var line in context.Match.Lines.Where(l => l.PurchaseOrderLine is null))
        {
            var amount = Math.Round((line.Line.Quantity ?? 0) * (line.Line.UnitPrice ?? 0), 2);
            yield return new Finding(DiscrepancyCode.ItemNotOnPo, Severity.Warning,
                line.Line.LineNumber, "a line on the purchase order", line.Line.Description ?? line.Line.ItemCode, amount);
        }
    }
}
