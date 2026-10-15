using ThreeWayMatch.Domain.Extraction;

namespace ThreeWayMatch.Domain.Normalisation;

/// <summary>
/// Checks that an invoice adds up on its own: every line is quantity times unit price, the lines add up to the net total,
/// and net plus VAT equals the gross total. When all of that holds, a low model confidence on those fields is not
/// a reason to doubt them: independent arithmetic backs the values.
/// </summary>
public static class InvoiceReconciliation
{
    /// <summary>
    /// True only when every figure involved is present, was not distrusted by the cross-check (confidence above 0),
    /// and all three sums agree within <paramref name="tolerance"/>. An invoice without lines never reconciles.
    /// </summary>
    public static bool Reconciles(ExtractedInvoice invoice, decimal tolerance)
    {
        if (invoice.Lines.Count == 0
            || !TryRead(invoice.NetTotal, out var net)
            || !TryRead(invoice.VatTotal, out var vat)
            || !TryRead(invoice.GrossTotal, out var gross))
        {
            return false;
        }

        var lineSum = 0m;
        foreach (var line in invoice.Lines)
        {
            if (!TryRead(line.Quantity, out var quantity)
                || !TryRead(line.UnitPrice, out var unitPrice)
                || !TryRead(line.LineTotal, out var lineTotal)
                || Math.Abs(quantity * unitPrice - lineTotal) > tolerance)
            {
                return false;
            }

            lineSum += lineTotal;
        }

        return Math.Abs(lineSum - net) <= tolerance && Math.Abs(net + vat - gross) <= tolerance;
    }

    /// <summary>A figure counts only if it has a value and the cross-check did not set its confidence to 0.</summary>
    private static bool TryRead(ExtractedField<decimal?> field, out decimal value)
    {
        value = field.Value ?? 0m;
        return field.Value is not null && field.Confidence is > 0;
    }
}
