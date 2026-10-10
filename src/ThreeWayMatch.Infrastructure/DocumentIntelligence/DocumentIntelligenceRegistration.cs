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
        IAnalysisClient analysis = new AzureAnalysisClient(client, options.InvoiceModelId, options.MinimumCallInterval);

        if (options.CacheDirectory is not null)
        {
            analysis = new CachingAnalysisClient(analysis, options.CacheDirectory, options.InvoiceModelId);
        }

        return new DocumentIntelligenceExtractor(analysis, options.InvoiceModelId);
    }
}
