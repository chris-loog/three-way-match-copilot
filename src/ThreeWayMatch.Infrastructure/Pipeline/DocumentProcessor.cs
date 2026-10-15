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
    DocumentMatcher matcher,
    TimeProvider clock,
    ILogger<DocumentProcessor> logger)
{
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
            await ExtractAsync(document, content, cancellationToken);
            var status = await matcher.MatchAsync(document.Id, cancellationToken);
            LogProcessed(logger, timer.ElapsedMilliseconds, status);
            return status;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !IsTransient(ex))
        {
            LogFailed(logger, ex);
            return await FailAsync(document.Id, ex.Message, cancellationToken);
        }
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
        document.Extracted = document.Type == DocumentType.DeliveryNote
            ? ResultMapper.ToEntity(await extractor.ExtractDeliveryNoteAsync(stream, cancellationToken), clock.GetUtcNow())
            : ResultMapper.ToEntity(await extractor.ExtractInvoiceAsync(stream, cancellationToken), clock.GetUtcNow());
        document.Status = DocumentStatus.Extracted;
        document.ErrorMessage = null;
        await db.SaveChangesAsync(cancellationToken);
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



}
