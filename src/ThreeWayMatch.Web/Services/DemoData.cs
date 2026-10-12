using Microsoft.EntityFrameworkCore;
using ThreeWayMatch.Infrastructure.Persistence;

namespace ThreeWayMatch.Web.Services;

/// <summary>Demo mode: clears everything the app created (documents, results, corrections, learned aliases). The mock ERP stays.</summary>
public sealed class DemoData(ThreeWayMatchDbContext db)
{
    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        var documents = await db.Documents
            .Include(d => d.Extracted).ThenInclude(e => e!.Lines)
            .Include(d => d.MatchResult).ThenInclude(m => m!.LineMatches).ThenInclude(l => l.GoodsReceiptLines)
            .Include(d => d.MatchResult).ThenInclude(m => m!.Discrepancies)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        db.FieldCorrections.RemoveRange(await db.FieldCorrections.ToListAsync(cancellationToken));
        db.ItemAliases.RemoveRange(await db.ItemAliases.ToListAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);

        // Line matches point at extracted lines, so the results go first, then the documents with their readings.
        db.MatchResults.RemoveRange(documents.Where(d => d.MatchResult is not null).Select(d => d.MatchResult!));
        await db.SaveChangesAsync(cancellationToken);

        db.Documents.RemoveRange(documents);
        await db.SaveChangesAsync(cancellationToken);
    }
}
