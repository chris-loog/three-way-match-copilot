using Microsoft.EntityFrameworkCore;
using ThreeWayMatch.Infrastructure.Persistence;

namespace DocGen.Seeding;

/// <summary>Writes an <see cref="ErpData"/> set into the <c>erp.</c> tables, replacing what was there.</summary>
internal static class ErpSeeder
{
    public static async Task ReplaceAsync(ThreeWayMatchDbContext db, ErpData data, CancellationToken cancellationToken)
    {
        // The Azure SQL free tier pauses when idle, so the context retries; the whole unit of work must be retryable.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            await EnsureNoApplicationDataReferencesErpAsync(db, cancellationToken);
            await ClearErpAsync(db, cancellationToken);

            db.Suppliers.AddRange(data.Suppliers);
            db.Items.AddRange(data.Items);
            db.SupplierItems.AddRange(data.SupplierItems);
            db.PurchaseOrders.AddRange(data.PurchaseOrders);
            db.GoodsReceipts.AddRange(data.GoodsReceipts);
            await db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        });
    }

    /// <summary>The seeder owns <c>erp.</c> only. If processed documents point at ERP rows, replacing the ERP would orphan them.</summary>
    private static async Task EnsureNoApplicationDataReferencesErpAsync(ThreeWayMatchDbContext db, CancellationToken cancellationToken)
    {
        var referenced = await db.MatchResults.AnyAsync(cancellationToken)
            || await db.LineMatches.AnyAsync(cancellationToken)
            || await db.ItemAliases.AnyAsync(cancellationToken);

        if (referenced)
        {
            throw new InvalidOperationException(
                "ap.MatchResult, ap.LineMatch or ap.ItemAlias already contain rows that reference the ERP data. " +
                "Delete those rows first; the seeder never touches the ap schema.");
        }
    }

    private static async Task ClearErpAsync(ThreeWayMatchDbContext db, CancellationToken cancellationToken)
    {
        // Children before parents.
        await db.GoodsReceiptLines.ExecuteDeleteAsync(cancellationToken);
        await db.GoodsReceipts.ExecuteDeleteAsync(cancellationToken);
        await db.PurchaseOrderLines.ExecuteDeleteAsync(cancellationToken);
        await db.PurchaseOrders.ExecuteDeleteAsync(cancellationToken);
        await db.SupplierItems.ExecuteDeleteAsync(cancellationToken);
        await db.Items.ExecuteDeleteAsync(cancellationToken);
        await db.Suppliers.ExecuteDeleteAsync(cancellationToken);
    }
}
