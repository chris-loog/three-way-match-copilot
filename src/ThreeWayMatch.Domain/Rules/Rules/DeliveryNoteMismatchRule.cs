using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Normalisation;

namespace ThreeWayMatch.Domain.Rules.Rules;

/// <summary>
/// DELIVERY_NOTE_MISMATCH: the quantity on the supplier's delivery note differs from what the warehouse booked for that
/// delivery. Compared exactly: a warehouse that books 9 where the note says 10 has a problem however small it looks.
/// A note whose receipt has not been booked yet is not compared (arrival order must not matter).
/// </summary>
public sealed class DeliveryNoteMismatchRule : IDiscrepancyRule
{
    private const decimal Epsilon = 0.0001m;

    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        if (context.Kind != DocumentType.DeliveryNote || string.IsNullOrWhiteSpace(context.DeliveryNoteNumber))
        {
            yield break;
        }

        var noteKey = Normaliser.ReferenceKey(context.DeliveryNoteNumber);
        foreach (var group in context.Match.Lines.Where(l => l.PurchaseOrderLine is not null && l.SupplierItem is not null).GroupBy(l => l.PurchaseOrderLine!))
        {
            var first = group.First();
            if (group.Any(l => l.ConvertedQuantity is null))
            {
                continue;
            }

            var booked = group.Key.ReceiptLines
                .Where(r => r.GoodsReceipt is not null && Normaliser.ReferenceKey(r.GoodsReceipt.DeliveryNoteNumber) == noteKey)
                .ToList();
            if (booked.Count == 0)
            {
                continue;
            }

            var factor = first.SupplierItem!.ConversionFactorToBase > 0 ? first.SupplierItem.ConversionFactorToBase : 1m;
            var bookedQuantity = booked.Sum(r => r.Quantity) * factor;
            var noteQuantity = first.GroupConvertedQuantity ?? 0;
            if (Math.Abs(noteQuantity - bookedQuantity) > Epsilon)
            {
                var amount = Math.Round(Math.Abs(noteQuantity - bookedQuantity) / factor * group.Key.UnitPrice, 2);
                yield return new Finding(DiscrepancyCode.DeliveryNoteMismatch, Severity.Warning, group.Min(l => l.Line.LineNumber),
                    Tolerances.Text(bookedQuantity), Tolerances.Text(noteQuantity), amount);
            }
        }
    }
}
