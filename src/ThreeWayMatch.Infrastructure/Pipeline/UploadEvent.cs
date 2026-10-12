using System.Text.Json;
using ThreeWayMatch.Domain.Ap;

namespace ThreeWayMatch.Infrastructure.Pipeline;

/// <summary>
/// The Event Grid message that lands on the <c>documents</c> queue when a file is uploaded to <c>incoming</c>.
/// The document type comes from the upload folder: <c>invoices/</c> or <c>delivery-notes/</c>.
/// </summary>
public sealed record UploadEvent(string Container, string BlobName, DocumentType Type)
{
    private const string BlobCreated = "Microsoft.Storage.BlobCreated";
    private const string Marker = "/blobs/";

    /// <summary>The path stored on <c>ap.Document</c>, e.g. <c>incoming/invoices/INV-1001.pdf</c>.</summary>
    public string BlobPath => $"{Container}/{BlobName}";

    /// <summary>
    /// Reads the message. Returns false, with the reason, for anything that is not a BlobCreated event for a known folder.
    /// Such a message can never succeed, so the caller should drop it instead of retrying.
    /// </summary>
    public static bool TryParse(string message, out UploadEvent? upload, out string reason)
    {
        upload = null;
        try
        {
            using var json = JsonDocument.Parse(message);
            var root = json.RootElement;

            // Event Grid schema uses eventType; CloudEvents uses type. Both carry the blob in subject.
            var eventType = Text(root, "eventType") ?? Text(root, "type");
            var subject = Text(root, "subject");
            if (eventType != BlobCreated || subject is null)
            {
                reason = $"Not a BlobCreated event (type '{eventType}').";
                return false;
            }

            // /blobServices/default/containers/incoming/blobs/invoices/INV-1001.pdf
            const string containers = "/containers/";
            var containerStart = subject.IndexOf(containers, StringComparison.Ordinal);
            var blobStart = subject.IndexOf(Marker, StringComparison.Ordinal);
            if (containerStart < 0 || blobStart < containerStart)
            {
                reason = $"Cannot read the blob from subject '{subject}'.";
                return false;
            }

            var container = subject[(containerStart + containers.Length)..blobStart];
            var name = subject[(blobStart + Marker.Length)..];

            DocumentType? type = name switch
            {
                _ when name.StartsWith("invoices/", StringComparison.OrdinalIgnoreCase) => DocumentType.Invoice,
                _ when name.StartsWith("delivery-notes/", StringComparison.OrdinalIgnoreCase) => DocumentType.DeliveryNote,
                _ => null,
            };
            if (container != "incoming" || type is null)
            {
                reason = $"'{container}/{name}' is not under incoming/invoices/ or incoming/delivery-notes/.";
                return false;
            }

            upload = new UploadEvent(container, name, type.Value);
            reason = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            reason = "The message is not valid JSON.";
            return false;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
