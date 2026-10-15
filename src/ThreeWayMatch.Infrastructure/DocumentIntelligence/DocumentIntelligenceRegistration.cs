using Azure.AI.DocumentIntelligence;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using ThreeWayMatch.Domain.Extraction;

namespace ThreeWayMatch.Infrastructure.DocumentIntelligence;

public static class DocumentIntelligenceRegistration
{
    /// <summary>
    /// Registers <see cref="IDocumentExtractor"/>. Authentication is <see cref="DefaultAzureCredential"/>:
    /// your <c>az login</c> on your machine, the managed identity in Azure. There is no key anywhere.
    /// </summary>
    public static IServiceCollection AddDocumentIntelligence(this IServiceCollection services, DocumentIntelligenceOptions options)
    {
        services.AddSingleton<IDocumentExtractor>(_ => Create(options));
        return services;
    }

    /// <summary>Builds the extractor without a service container, for tools and scripts.</summary>
    public static IDocumentExtractor Create(DocumentIntelligenceOptions options)
    {
        var client = new DocumentIntelligenceClient(options.Endpoint, new DefaultAzureCredential());

        return new DocumentIntelligenceExtractor(
            Analysis(client, options, options.InvoiceModelId, useQueryFields: true), options.InvoiceModelId,
            Analysis(client, options, options.DeliveryNoteModelId, useQueryFields: false), options.DeliveryNoteModelId);
    }

    private static IAnalysisClient Analysis(DocumentIntelligenceClient client, DocumentIntelligenceOptions options, string modelId, bool useQueryFields)
    {
        IAnalysisClient analysis = options.CacheOnly
            ? new MissingFromCacheClient(modelId)
            : new AzureAnalysisClient(client, modelId, options.MinimumCallInterval, useQueryFields);
        return options.CacheDirectory is null ? analysis : new CachingAnalysisClient(analysis, options.CacheDirectory, modelId);
    }
}

/// <summary>The inner client of a cache-only run: reaching it means the document was never analysed with this model.</summary>
internal sealed class MissingFromCacheClient(string modelId) : IAnalysisClient
{
    public Task<string> AnalyzeAsync(ReadOnlyMemory<byte> document, CancellationToken cancellationToken) =>
        throw new InvalidOperationException($"This document has no cached result for model '{modelId}', and this run may not call Azure.");
}
