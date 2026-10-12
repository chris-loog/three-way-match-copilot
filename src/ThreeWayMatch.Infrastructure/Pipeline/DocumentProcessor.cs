using System.Diagnostics;
using Azure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Domain.Rules;
using ThreeWayMatch.Infrastructure.DocumentIntelligence;
using ThreeWayMatch.Infrastructure.Persistence;

namespace ThreeWayMatch.Infrastructure.Pipeline;

/// <summary>
/// Takes one uploaded file through the pipeline: record it, read it, match it, decide, save.
/// Every stage is saved before the next one starts and skipped when already done, so a retried queue message
/// resumes where the last attempt stopped instead of starting over (and extraction results come from the hash cache anyway).
/// </summary>
public sealed partial class DocumentProcessor(
    ThreeWayMatchDbContext db,
    IDocumentExtractor extractor,
    DocumentAssessor assessor,
    SnapshotLoader snapshotLoader,
    DiscrepancyOptions options,
    TimeProvider clock,
    ILogger<DocumentProcessor> logger)
{
    private const int MaxConcurrencyAttempts = 3;

    private static readonly DocumentStatus[] FinishedStatuses =
        [DocumentStatus.AutoApproved, DocumentStatus.OnHold, DocumentStatus.NeedsReview, DocumentStatus.Rejected];

    /// <summary>
    /// Processes the file uploaded at <paramref name="blobPath"/>. Returns the document's status afterwards.
    /// A temporary problem (Document Intelligence busy, database unreachable) is thrown, so the queue retries the message.
    /// A permanent one (unreadable file) ends as status <see cref="DocumentStatus.Failed"/> with the error saved.
    /// </summary>
    public async Task<DocumentStatus> ProcessAsync(string blobPath, DocumentType type, byte[] content, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var hash = ContentHash.Sha256Hex(content);
        var document = await FindOrCreateAsync(blobPath, type, hash, cancellationToken);

        using var scope = logger.BeginScope(new Dictionary<string, object> { ["DocumentId"] = document.Id, ["BlobPath"] = blobPath });

        if (document.ContentHash != hash)
        {
            LogOverwritten(logger);
            return document.Status;
        }

        if (FinishedStatuses.Contains(document.Status))
        {
            LogSkipped(logger, document.Status);
            return document.Status;
        }

        try
        {
            if (type != DocumentType.Invoice)
            {
                return await FailAsync(document.Id, "Delivery notes cannot be read yet: the delivery-note model comes with step 4.1.", cancellationToken);
            }

            await ExtractAsync(document, content, cancellationToken);
            var status = await MatchAsync(document.Id, cancellationToken);
            LogProcessed(logger, timer.ElapsedMilliseconds, status);
            return status;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !IsTransient(ex))
        {
            LogFailed(logger, ex);
            return await FailAsync(document.Id, ex.Message, cancellationToken);
        }
    }

    /// <summary>Re-runs matching for every OnHold document, e.g. after a goods receipt arrived. Uses the stored extraction; nothing is read again.</summary>
    public async Task<int> RematchOnHoldAsync(CancellationToken cancellationToken)
    {
        var ids = await db.Documents.AsNoTracking().Where(d => d.Status == DocumentStatus.OnHold).Select(d => d.Id).ToListAsync(cancellationToken);
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

    private async Task<Document> FindOrCreateAsync(string blobPath, DocumentType type, string hash, CancellationToken cancellationToken)
    {
        var existing = await db.Documents.FirstOrDefaultAsync(d => d.BlobPath == blobPath, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var document = new Document
        {
            BlobPath = blobPath, Type = type, Status = DocumentStatus.Received, ContentHash = hash, ReceivedAt = clock.GetUtcNow(),
        };
        db.Documents.Add(document);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return document;
        }
        catch (DbUpdateException)
        {
            // Two deliveries of the same message created the row at once. The other one won; use its row.
            db.Entry(document).State = EntityState.Detached;
            return await db.Documents.FirstOrDefaultAsync(d => d.BlobPath == blobPath, cancellationToken) ?? throw new InvalidOperationException($"Could not record {blobPath}.");
        }
    }

    private async Task ExtractAsync(Document document, byte[] content, CancellationToken cancellationToken)
    {
        if (await db.ExtractedDocuments.AnyAsync(e => e.DocumentId == document.Id, cancellationToken))
        {
            return;
        }

        using var stream = new MemoryStream(content, writable: false);
        var invoice = await extractor.ExtractInvoiceAsync(stream, cancellationToken);

        document.Extracted = ResultMapper.ToEntity(invoice, clock.GetUtcNow());
        document.Status = DocumentStatus.Extracted;
        document.ErrorMessage = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Matches the stored extraction and saves the decision. If another invoice claimed the same PO line meanwhile,
    /// the save fails on the row version and the whole step runs again with fresh data, so the second invoice sees the first.
    /// </summary>
    private async Task<DocumentStatus> MatchAsync(int documentId, CancellationToken cancellationToken)
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

    private async Task<DocumentStatus> MatchOnceAsync(int documentId, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var document = await db.Documents
            .Include(d => d.Extracted).ThenInclude(e => e!.Lines)
            .Include(d => d.MatchResult).ThenInclude(m => m!.LineMatches).ThenInclude(l => l.GoodsReceiptLines)
            .Include(d => d.MatchResult).ThenInclude(m => m!.Discrepancies)
            .SingleAsync(d => d.Id == documentId, cancellationToken);
        var extracted = document.Extracted ?? throw new InvalidOperationException("The document has no extraction yet.");

        // Map again from the stored raw result: the same code as on first read, and no call to Azure.
        var invoice = InvoiceMapper.Map(extracted.RawJson, extracted.ModelId);
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
            ResultMapper.ApplyConfidence(extracted, checkedInvoice);
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

    private async Task<DocumentStatus> FailAsync(int documentId, string message, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var document = await db.Documents.SingleAsync(d => d.Id == documentId, cancellationToken);
        document.Status = DocumentStatus.Failed;
        document.ErrorMessage = message.Length > 2000 ? message[..2000] : message;
        await db.SaveChangesAsync(cancellationToken);
        return DocumentStatus.Failed;
    }

    /// <summary>
    /// Worth retrying later: the service was busy or unreachable. Anything else (a corrupt PDF, a bug) will fail the same way again.
    /// </summary>
    internal static bool IsTransient(Exception exception) => exception switch
    {
        RequestFailedException { Status: 408 or 429 or >= 500 } => true,
        HttpRequestException or TimeoutException or SqlException or DbUpdateConcurrencyException or RetryLimitExceededException => true,
        _ => false,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "The blob was overwritten with different content after it was processed. Upload changed files under a new name. Nothing was changed.")]
    private static partial void LogOverwritten(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Already processed ({Status}); skipping.")]
    private static partial void LogSkipped(ILogger logger, DocumentStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processed in {ElapsedMs} ms: {Status}.")]
    private static partial void LogProcessed(ILogger logger, long elapsedMs, DocumentStatus status);

    [LoggerMessage(Level = LogLevel.Error, Message = "Processing failed permanently.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Re-check of document {DocumentId} failed; it stays on hold.")]
    private static partial void LogRematchFailed(ILogger logger, Exception exception, int documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Re-checked {Count} documents on hold; {Released} moved on.")]
    private static partial void LogRematched(ILogger logger, int count, int released);

    [LoggerMessage(Level = LogLevel.Information, Message = "Another invoice changed the same order lines; matching again (attempt {Attempt}).")]
    private static partial void LogConcurrency(ILogger logger, int attempt);
}
