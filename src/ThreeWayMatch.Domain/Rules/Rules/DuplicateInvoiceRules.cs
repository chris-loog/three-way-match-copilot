using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Normalisation;

namespace ThreeWayMatch.Domain.Rules.Rules;

/// <summary>DUPLICATE_INVOICE: the same file again (same content hash) or the same invoice number from the same supplier. The document is rejected.</summary>
public sealed class DuplicateInvoiceRule : IDiscrepancyRule
{
    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        if (context.Kind != DocumentType.Invoice || context.Invoice is null)
        {
            yield break;
        }

        var key = Normaliser.ReferenceKey(context.Invoice.InvoiceNumber.Value);
        var sameFile = context.ProcessedInvoices.Any(p => context.ContentHash is not null && p.ContentHash == context.ContentHash);
        var sameNumber = key.Length > 0 && context.Supplier is { } supplier
            && context.ProcessedInvoices.Any(p => p.SupplierId == supplier.Id && Normaliser.ReferenceKey(p.InvoiceNumber) == key);

        if (sameFile || sameNumber)
        {
            yield return new Finding(DiscrepancyCode.DuplicateInvoice, Severity.Block, null,
                "a new invoice", sameFile ? "same file as an earlier invoice" : $"invoice {context.Invoice.InvoiceNumber.Value} already processed",
                context.Invoice.GrossTotal.Value ?? 0m);
        }
    }
}

/// <summary>
/// POSSIBLE_DUPLICATE: same supplier, same amount, dates close together, but a different invoice number. Typical for a
/// resent invoice with a new number. Never rejected automatically: a person decides. Two invoices for different orders are
/// not suspect (standard services cost the same every time); if either order is unknown, the doubt stays.
/// </summary>
public sealed class PossibleDuplicateRule : IDiscrepancyRule
{
    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        if (context.Kind != DocumentType.Invoice || context.Invoice is not { } invoice || context.Supplier is not { } supplier
            || invoice.GrossTotal.Value is not { } gross || invoice.InvoiceDate.Value is not { } date)
        {
            yield break;
        }

        var key = Normaliser.ReferenceKey(invoice.InvoiceNumber.Value);
        var currentOrderId = context.Match.PurchaseOrder.PurchaseOrder?.Id;
        var earlier = context.ProcessedInvoices.FirstOrDefault(p =>
            p.SupplierId == supplier.Id
            && !(p.PurchaseOrderId is { } earlierOrder && currentOrderId is { } thisOrder && earlierOrder != thisOrder)
            && p.GrossTotal is { } amount && Math.Abs(amount - gross) < 0.005m
            && p.InvoiceDate is { } other && Math.Abs(other.DayNumber - date.DayNumber) <= context.Options.PossibleDuplicateWindowDays
            && Normaliser.ReferenceKey(p.InvoiceNumber) != key
            && (context.ContentHash is null || p.ContentHash != context.ContentHash));

        if (earlier is not null)
        {
            yield return new Finding(DiscrepancyCode.PossibleDuplicate, Severity.Warning, null,
                earlier.InvoiceNumber, "new number, same supplier and amount", gross);
        }
    }
}
