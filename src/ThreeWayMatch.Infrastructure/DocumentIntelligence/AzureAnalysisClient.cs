using System.ClientModel.Primitives;
using Azure;
using Azure.AI.DocumentIntelligence;

namespace ThreeWayMatch.Infrastructure.DocumentIntelligence;

/// <summary>The only class that calls Azure. Everything it needs to know about the model is in its two constructor arguments.</summary>
internal sealed class AzureAnalysisClient(DocumentIntelligenceClient client, string modelId, TimeSpan minimumCallInterval) : IAnalysisClient
{
    // The prebuilt invoice model has no field for the bank account or the registry code.
    // The query fields add-on asks the model to find them anyway.
    private static readonly string[] QueryFieldNames = ["Iban", "RegistryCode"];

    private readonly object _slotLock = new();
    private DateTimeOffset _nextSlot = DateTimeOffset.MinValue;

    public async Task<string> AnalyzeInvoiceAsync(ReadOnlyMemory<byte> document, CancellationToken cancellationToken)
    {
        await WaitForTurnAsync(cancellationToken);

        var options = new AnalyzeDocumentOptions(modelId, BinaryData.FromBytes(document));
        options.Features.Add(DocumentAnalysisFeature.QueryFields);
        foreach (var name in QueryFieldNames)
        {
            options.QueryFields.Add(name);
        }

        Operation<AnalyzeResult> operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, options, cancellationToken);

        // The SDK model serialises back to the same JSON shape the service returns, and can be read from it again.
        return ModelReaderWriter.Write(operation.Value).ToString();
    }

    /// <summary>
    /// Spaces calls at least <c>minimumCallInterval</c> apart, even when several run at once: each caller reserves
    /// the next free time slot and sleeps until it.
    /// </summary>
    private async Task WaitForTurnAsync(CancellationToken cancellationToken)
    {
        if (minimumCallInterval <= TimeSpan.Zero)
        {
            return;
        }

        TimeSpan wait;
        lock (_slotLock)
        {
            var now = DateTimeOffset.UtcNow;
            var start = _nextSlot > now ? _nextSlot : now;
            _nextSlot = start + minimumCallInterval;
            wait = start - now;
        }

        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken);
        }
    }
}
