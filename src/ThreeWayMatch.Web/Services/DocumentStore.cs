using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using ThreeWayMatch.Domain.Ap;

namespace ThreeWayMatch.Web.Services;

/// <summary>Where uploaded documents live. The review UI uploads here and shows previews from here.</summary>
public interface IDocumentStore
{
    /// <summary>Saves the file under <c>incoming/invoices/</c> or <c>incoming/delivery-notes/</c> and returns its blob path.</summary>
    Task<string> UploadAsync(DocumentType type, string fileName, Stream content, CancellationToken cancellationToken);

    /// <summary>A link that works for a few minutes, or null when previews are not set up.</summary>
    Task<Uri?> PreviewUrlAsync(string blobPath, CancellationToken cancellationToken);
}

public sealed partial class BlobDocumentStore(BlobServiceClient service) : IDocumentStore
{
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(5);

    public async Task<string> UploadAsync(DocumentType type, string fileName, Stream content, CancellationToken cancellationToken)
    {
        var folder = type == DocumentType.Invoice ? "invoices" : "delivery-notes";
        var safe = Unsafe().Replace(Path.GetFileName(fileName), "-");

        // The time and a random part keep two uploads of the same file name apart: the blob path identifies a document.
        var name = $"{folder}/{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}-{safe}";
        await service.GetBlobContainerClient("incoming").GetBlobClient(name).UploadAsync(content, overwrite: false, cancellationToken);
        return $"incoming/{name}";
    }

    /// <summary>
    /// A read-only link signed with an Entra ID user-delegation key, not the storage account key, so no key exists to leak.
    /// </summary>
    public async Task<Uri?> PreviewUrlAsync(string blobPath, CancellationToken cancellationToken)
    {
        var slash = blobPath.IndexOf('/', StringComparison.Ordinal);
        if (slash < 0)
        {
            return null;
        }

        var (container, name) = (blobPath[..slash], blobPath[(slash + 1)..]);
        var now = DateTimeOffset.UtcNow;
        var key = await service.GetUserDelegationKeyAsync(now.AddMinutes(-1), now.Add(LinkLifetime), cancellationToken);

        var sas = new BlobSasBuilder(BlobSasPermissions.Read, now.Add(LinkLifetime))
        {
            BlobContainerName = container,
            BlobName = name,
            Resource = "b",
            StartsOn = now.AddMinutes(-1),
        };
        var uri = new BlobUriBuilder(service.GetBlobContainerClient(container).GetBlobClient(name).Uri)
        {
            Sas = sas.ToSasQueryParameters(key.Value, service.AccountName),
        };
        return uri.ToUri();
    }

    [GeneratedRegex(@"[^A-Za-z0-9._-]")]
    private static partial Regex Unsafe();
}

/// <summary>Used when no storage is configured (local runs, tests): uploads are refused and previews are missing.</summary>
public sealed class NoDocumentStore : IDocumentStore
{
    public Task<string> UploadAsync(DocumentType type, string fileName, Stream content, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Storage is not configured. Set Storage:BlobServiceUri.");

    public Task<Uri?> PreviewUrlAsync(string blobPath, CancellationToken cancellationToken) => Task.FromResult<Uri?>(null);
}
