using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Rules;
using ThreeWayMatch.Infrastructure.DocumentIntelligence;
using ThreeWayMatch.Infrastructure.Persistence;

namespace ThreeWayMatch.Infrastructure.Pipeline;

/// <summary>
/// The matching stage on its own: takes a document whose extraction is stored, judges it and saves the decision.
/// The pipeline calls it after extraction; the daily timer calls it for documents on hold; the review UI calls it
/// after a reviewer corrects a field. It never reads a document again, so it needs no Document Intelligence.
/// </summary>
public sealed partial class DocumentMatcher(
    ThreeWayMatchDbContext db,
    DocumentAssessor assessor,
    SnapshotLoader snapshotLoader,
    DiscrepancyOptions options,
    TimeProvider clock,
    ILogger<DocumentMatcher> logger)
{
    private const int MaxConcurrencyAttempts = 3;

    /// <summary>
    /// Matches the stored extraction (with the reviewer's corrections on top) and saves the decision.
    /// If another invoice claimed the same PO line meanwhile, the save fails on the row version and the whole
    /// step runs again with fresh data, so the second invoice sees the first.
    /// </summary>
    public async Task<DocumentStatus> MatchAsync(int documentId, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var strategy = db.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(() => MatchOnceAsync(documentId, cancellationToken));
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyAttempts)
            {
                LogConcurrency(logger, attempt + 1);
            }
        }
    }

    /// <summary>Re-runs matching for every OnHold document nobody has acted on, e.g. after a goods receipt arrived.</summary>
    public async Task<int> RematchOnHoldAsync(CancellationToken cancellationToken)
    {
        var ids = await db.Documents.AsNoTracking()
            .Where(d => d.Status == DocumentStatus.OnHold && (d.MatchResult == null || d.MatchResult.ReviewedAt == null))
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        var released = 0;
        foreach (var id in ids)
        {
            try
            {
                if (await MatchAsync(id, cancellationToken) != DocumentStatus.OnHold)
                {
                    released++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRematchFailed(logger, ex, id);
            }
        }

        LogRematched(logger, ids.Count, released);
        return released;
    }

    private async Task<DocumentStatus> MatchOnceAsync(int documentId, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var document = await db.Documents
            .Include(d => d.Extracted).ThenInclude(e => e!.Lines)
            .Include(d => d.MatchResult).ThenInclude(m => m!.LineMatches).ThenInclude(l => l.GoodsReceiptLines)
            .Include(d => d.MatchResult).ThenInclude(m => m!.Discrepancies)
            .SingleAsync(d => d.Id == documentId, cancellationToken);
        var extracted = document.Extracted ?? throw new InvalidOperationException("The document has no extraction yet.");

        if (document.MatchResult is { ReviewedAt: not null })
        {
            throw new InvalidOperationException("A reviewer has already acted on this document; it is not matched again.");
        }

        // Map again from the stored raw result (the same code as on first read, no call to Azure), then put the corrections on top.
        var corrections = await db.FieldCorrections.AsNoTracking().Where(c => c.ExtractedDocumentId == extracted.Id).ToListAsync(cancellationToken);
        var invoice = CorrectionApplier.Apply(InvoiceMapper.Map(extracted.RawJson, extracted.ModelId), corrections);
        var snapshot = await snapshotLoader.LoadAsync(documentId, cancellationToken);
        var processed = await snapshotLoader.LoadProcessedInvoicesAsync(documentId, cancellationToken);
        var assessment = assessor.AssessInvoice(invoice, document.ContentHash, snapshot, processed, options);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (document.MatchResult is { } previous)
        {
            db.MatchResults.Remove(previous);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (assessment.CheckedInvoice is { } checkedInvoice)
        {
            ResultMapper.ApplyChecked(extracted, checkedInvoice);
        }

        if (assessment.Decision == Decision.AutoApproved)
        {
            await ClaimPurchaseOrderLinesAsync(assessment, cancellationToken);
        }

        var receiptIds = assessment.Match.Lines.SelectMany(l => l.ReceiptLines).Select(r => r.Id).Distinct().ToList();
        var receiptLines = await db.GoodsReceiptLines.Where(r => receiptIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);

        document.MatchResult = ResultMapper.ToEntity(assessment, extracted, receiptLines, clock.GetUtcNow());
        document.Status = ResultMapper.ToStatus(assessment.Decision);
        document.ErrorMessage = null;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return document.Status;
    }

    /// <summary>
    /// An approved invoice uses up quantity on its PO lines. Touching each line makes SQL Server bump its row version,
    /// and the update only succeeds if nobody changed the line since the snapshot was read.
    /// </summary>
    private async Task ClaimPurchaseOrderLinesAsync(Assessment assessment, CancellationToken cancellationToken)
    {
        var snapshotLines = assessment.Match.Lines
            .Where(l => l.PurchaseOrderLine is not null)
            .Select(l => l.PurchaseOrderLine!)
            .DistinctBy(l => l.Id)
            .ToList();
        var ids = snapshotLines.Select(l => l.Id).ToList();
        var current = await db.PurchaseOrderLines.Where(l => ids.Contains(l.Id)).ToListAsync(cancellationToken);

        foreach (var line in current)
        {
            var seen = snapshotLines.Single(l => l.Id == line.Id);
            if (!seen.RowVersion.SequenceEqual(line.RowVersion))
            {
                throw new DbUpdateConcurrencyException($"Purchase order line {line.Id} changed after the snapshot was read.");
            }

            db.Entry(line).Property(l => l.Status).IsModified = true;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Re-check of document {DocumentId} failed; it stays on hold.")]
    private static partial void LogRematchFailed(ILogger logger, Exception exception, int documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Re-checked {Count} documents on hold; {Released} moved on.")]
    private static partial void LogRematched(ILogger logger, int count, int released);

    [LoggerMessage(Level = LogLevel.Information, Message = "Another invoice changed the same order lines; matching again (attempt {Attempt}).")]
    private static partial void LogConcurrency(ILogger logger, int attempt);
}
