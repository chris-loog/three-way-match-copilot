using ThreeWayMatch.Domain.Erp;
using ThreeWayMatch.Domain.Matching;

namespace ThreeWayMatch.Domain.Rules;

internal enum QuantityIssueKind
{
    /// <summary>More invoiced than has arrived, and goods are still expected: wait.</summary>
    PartialDelivery,

    /// <summary>More invoiced than has arrived and nothing more is expected, or more than was ordered.</summary>
    OverInvoiced,
}

internal sealed record QuantityIssue(
    QuantityIssueKind Kind,
    int LineNumber,
    PurchaseOrderLine PurchaseOrderLine,
    decimal Invoiced,
    decimal OpenOnReceipts,
    decimal Amount);

/// <summary>
/// Compares what the document asks for with what has arrived, per order line. One analysis feeds both
/// QTY_OVER_INVOICED and PARTIAL_DELIVERY, so the two rules can never disagree about the same line.
/// </summary>
internal static class QuantityAnalysis
{
    public static IEnumerable<QuantityIssue> Find(RuleContext context)
    {
        var groups = context.Match.Lines
            .Where(l => l.PurchaseOrderLine is not null && l.Quantities is not null)
            .GroupBy(l => l.PurchaseOrderLine!);

        foreach (var group in groups)
        {
            // A line whose unit could not be converted has no reliable quantity; the unit rule reports it instead.
            if (group.Any(l => l.ConvertedQuantity is null))
            {
                continue;
            }

            var first = group.First();
            var quantities = first.Quantities!;
            var invoiced = first.GroupConvertedQuantity ?? 0;
            var line = group.Key;

            var tolerance = Tolerances.Quantity(quantities.OpenOnReceipts, context.Supplier, context.Options);
            if (invoiced <= quantities.OpenOnReceipts + tolerance)
            {
                continue;
            }

            // Goods still on their way, and the document does not ask for more than was ordered: it is early, not wrong.
            var orderTolerance = Tolerances.Quantity(quantities.OpenOnOrder, context.Supplier, context.Options);
            var stillExpected = quantities.Received < quantities.Ordered
                && line.Status != PurchaseOrderLineStatus.Closed
                && invoiced <= quantities.OpenOnOrder + orderTolerance;

            var factor = first.SupplierItem?.ConversionFactorToBase is > 0 ? first.SupplierItem.ConversionFactorToBase : 1m;
            var excessInSupplierUnits = (invoiced - quantities.OpenOnReceipts) / factor;

            yield return new QuantityIssue(
                stillExpected ? QuantityIssueKind.PartialDelivery : QuantityIssueKind.OverInvoiced,
                group.Min(l => l.Line.LineNumber),
                line,
                invoiced,
                quantities.OpenOnReceipts,
                Math.Round(excessInSupplierUnits * line.UnitPrice, 2));
        }
    }
}
