using Azure;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ThreeWayMatch.Infrastructure.Pipeline;

namespace ThreeWayMatch.Functions;

/// <summary>
/// Runs for every message on the <c>documents</c> queue. Event Grid puts one there when a file lands in <c>incoming</c>.
/// If this throws, the queue shows the message again after 30 s; after 5 failures it moves to <c>documents-poison</c>.
/// </summary>
public sealed partial class ProcessUploadFunction(DocumentProcessor processor, BlobServiceClient blobs, ILogger<ProcessUploadFunction> logger)
{
    [Function("ProcessUpload")]
    public async Task Run([QueueTrigger("documents", Connection = "Storage")] string message, CancellationToken cancellationToken)
    {
        if (!UploadEvent.TryParse(message, out var upload, out var reason))
        {
            // Retrying cannot fix a message like this, so it is dropped instead of ending in the poison queue.
            LogIgnored(logger, reason);
            return;
        }

        byte[] content;
        try
        {
            var blob = blobs.GetBlobContainerClient(upload!.Container).GetBlobClient(upload.BlobName);
            content = (await blob.DownloadContentAsync(cancellationToken)).Value.Content.ToArray();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            LogMissing(logger, upload!.BlobPath);
            return;
        }

        await processor.ProcessAsync(upload.BlobPath, upload.Type, content, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring a queue message: {Reason}")]
    private static partial void LogIgnored(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The blob {BlobPath} no longer exists; nothing to process.")]
    private static partial void LogMissing(ILogger logger, string blobPath);
}
