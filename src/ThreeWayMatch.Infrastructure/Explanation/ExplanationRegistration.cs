using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using ThreeWayMatch.Domain.Explanation;

namespace ThreeWayMatch.Infrastructure.Explanation;

public static class ExplanationRegistration
{
    /// <summary>
    /// Registers the explanation service. With an endpoint it uses Azure OpenAI through <c>DefaultAzureCredential</c>
    /// (your <c>az login</c> locally, the managed identity in Azure; no key anywhere). Without one it still works and shows the template text.
    /// </summary>
    public static IServiceCollection AddExplanations(this IServiceCollection services, ExplanationOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton(_ => CreateClient(options));
        services.AddScoped<ExplanationService>();
        return services;
    }

    /// <summary>Builds the model client without a service container, for tools and scripts.</summary>
    public static ILlmClient CreateClient(ExplanationOptions options) => options.Endpoint is null
        ? new UnavailableLlmClient()
        : new AzureOpenAiClient(new AzureOpenAIClient(options.Endpoint, new DefaultAzureCredential()), options);

    private sealed class UnavailableLlmClient : ILlmClient
    {
        public Task<LlmResult> CompleteJsonAsync(string systemPrompt, string userPrompt, string schemaName, string jsonSchema, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No Azure OpenAI endpoint is configured.");
    }
}
