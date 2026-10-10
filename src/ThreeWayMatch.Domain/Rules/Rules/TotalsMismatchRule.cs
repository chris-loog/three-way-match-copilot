using ThreeWayMatch.Domain.Ap;

namespace ThreeWayMatch.Domain.Rules.Rules;

/// <summary>
/// TOTALS_MISMATCH: the printed sums do not add up. Three checks, in order: the lines add up to the net total, the VAT of each
/// line (24 % unless the line says otherwise) adds up to the VAT total, and net plus VAT equals the gross total.
/// </summary>
public sealed class TotalsMismatchRule : IDiscrepancyRule
{
    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        if (context.Kind != DocumentType.Invoice || context.Invoice is not { } invoice)
        {
            yield break;
        }

        var tolerance = context.Options.TotalsTolerance;
        var lineTotals = invoice.Lines
            .Select(l => (Total: l.LineTotal.Value ?? (l.Quantity.Value * l.UnitPrice.Value), Rate: l.VatRate.Value ?? context.Options.StandardVatRate))
            .ToList();

        // Without a total there is nothing to compare; an unreadable total is a LOW_CONFIDENCE matter.
        if (invoice.NetTotal.Value is { } net && lineTotals.All(l => l.Total is not null))
        {
            var sum = lineTotals.Sum(l => l.Total!.Value);
            if (Math.Abs(sum - net) > tolerance)
            {
                yield return Mismatch("lines add up to the net total", sum, net);
                yield break;
            }
        }

        if (invoice.VatTotal.Value is { } vat && lineTotals.All(l => l.Total is not null))
        {
            var expected = lineTotals.Sum(l => Math.Round(l.Total!.Value * l.Rate, 2));
            if (Math.Abs(expected - vat) > tolerance)
            {
                yield return Mismatch("VAT of the lines adds up to the VAT total", expected, vat);
                yield break;
            }
        }

        if (invoice.NetTotal.Value is { } netTotal && invoice.VatTotal.Value is { } vatTotal && invoice.GrossTotal.Value is { } gross
            && Math.Abs(netTotal + vatTotal - gross) > tolerance)
        {
            yield return Mismatch("net plus VAT equals the gross total", netTotal + vatTotal, gross);
        }
    }

    private static Finding Mismatch(string check, decimal expected, decimal actual) =>
        new(DiscrepancyCode.TotalsMismatch, Severity.Warning, null,
            Tolerances.Text(expected), $"{Tolerances.Text(actual)} ({check})", Math.Round(Math.Abs(expected - actual), 2));
}
