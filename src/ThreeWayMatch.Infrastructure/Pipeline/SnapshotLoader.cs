using Microsoft.EntityFrameworkCore;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Matching;
using ThreeWayMatch.Domain.Rules;
using ThreeWayMatch.Infrastructure.Persistence;

namespace ThreeWayMatch.Infrastructure.Pipeline;

/// <summary>
/// Reads the ERP data the matching engine needs and hands it over as a plain <see cref="MatchingSnapshot"/>.
/// The engine never queries anything; this is the only place that knows how the snapshot maps to tables.
/// </summary>
public sealed class SnapshotLoader(ThreeWayMatchDbContext db)
{
    public async Task<MatchingSnapshot> LoadAsync(int currentDocumentId, CancellationToken cancellationToken)
    {
        // Identity resolution makes every row one shared object, so "the same supplier" is the same reference everywhere.
        var suppliers = await db.Suppliers.AsNoTrackingWithIdentityResolution().ToListAsync(cancellationToken);
        var supplierItems = await db.SupplierItems.AsNoTrackingWithIdentityResolution().Include(i => i.Item).ToListAsync(cancellationToken);
        var aliases = await db.ItemAliases.AsNoTrackingWithIdentityResolution().ToListAsync(cancellationToken);
        var orders = await db.PurchaseOrders.AsNoTrackingWithIdentityResolution()
            .Include(o => o.Lines).ThenInclude(l => l.SupplierItem)
            .Include(o => o.Lines).ThenInclude(l => l.ReceiptLines).ThenInclude(r => r.GoodsReceipt)
            .ToListAsync(cancellationToken);

        return new MatchingSnapshot
        {
            Suppliers = suppliers,
            SupplierItems = supplierItems,
            Aliases = aliases,
            PurchaseOrders = orders,
            InvoicedQuantityByPurchaseOrderLine = await InvoicedQuantitiesAsync(currentDocumentId, supplierItems.ToDictionary(i => i.Id), cancellationToken),
        };
    }

    /// <summary>
    /// Invoices processed before this one, for the duplicate rules. A failed document read nothing, so it counts for nothing.
    /// </summary>
    public async Task<IReadOnlyList<ProcessedInvoice>> LoadProcessedInvoicesAsync(int currentDocumentId, CancellationToken cancellationToken) =>
        await db.Documents.AsNoTracking()
            .Where(d => d.Type == DocumentType.Invoice && d.Id < currentDocumentId && d.Extracted != null)
            .Select(d => new ProcessedInvoice(
                d.ContentHash,
                d.MatchResult == null ? null : d.MatchResult.SupplierId,
                d.Extracted!.DocumentNumber,
                d.Extracted.GrossTotal,
                d.Extracted.DocumentDate,
                d.MatchResult == null ? null : d.MatchResult.PurchaseOrderId))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// What approved invoices already billed per PO line, in the PO line's unit. A match counts when the engine
    /// auto-approved it or a reviewer acted on it; documents still waiting, on hold, rejected or being re-processed do not.
    /// </summary>
    private async Task<Dictionary<int, decimal>> InvoicedQuantitiesAsync(
        int currentDocumentId, Dictionary<int, Domain.Erp.SupplierItem> supplierItems, CancellationToken cancellationToken)
    {
        var approved = await db.LineMatches.AsNoTracking()
            .Where(l => l.PurchaseOrderLineId != null && l.ConvertedQuantity != null
                && l.MatchResult!.DocumentId != currentDocumentId
                && (l.MatchResult.Decision == Decision.AutoApproved || l.MatchResult.ReviewedAt != null))
            .Select(l => new { PurchaseOrderLineId = l.PurchaseOrderLineId!.Value, SupplierItemId = l.PurchaseOrderLine!.SupplierItemId, Base = l.ConvertedQuantity!.Value })
            .ToListAsync(cancellationToken);

        // ConvertedQuantity is in the base unit; the snapshot wants the PO line's unit, so divide by the supplier's factor.
        return approved
            .GroupBy(a => a.PurchaseOrderLineId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(a => supplierItems[a.SupplierItemId].ConversionFactorToBase is var factor and > 0 ? a.Base / factor : a.Base));
    }
}
