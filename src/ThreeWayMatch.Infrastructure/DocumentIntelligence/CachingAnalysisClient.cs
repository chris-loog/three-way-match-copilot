using ThreeWayMatch.Domain.Extraction;

namespace ThreeWayMatch.Infrastructure.DocumentIntelligence;

/// <summary>
/// Saves every raw result in a folder, named after the SHA-256 of the document and the model that read it.
/// The same file is analysed once, however often the code runs. The hash is also what the duplicate check uses later.
/// </summary>
internal sealed class CachingAnalysisClient(IAnalysisClient inner, string directory, string modelId) : IAnalysisClient
{
    public async Task<string> AnalyzeInvoiceAsync(ReadOnlyMemory<byte> document, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{ContentHash.Sha256Hex(document.Span)}.{modelId}.json");

        if (File.Exists(path))
        {
            return await File.ReadAllTextAsync(path, cancellationToken);
        }

        var json = await inner.AnalyzeInvoiceAsync(document, cancellationToken);

        // Write to a temporary name first so a crash never leaves half a file that later reads as a result.
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, json, cancellationToken);
        File.Move(temporary, path, overwrite: true);
        return json;
    }
}
