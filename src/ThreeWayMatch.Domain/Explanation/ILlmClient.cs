namespace ThreeWayMatch.Domain.Explanation;

/// <summary>What the model answered, and what the call cost in tokens. Logged so the cost per document can be read from Application Insights.</summary>
public sealed record LlmResult(string Text, int PromptTokens, int CompletionTokens, string Model);

/// <summary>
/// A chat model that must answer with JSON matching a schema. The explanation code only knows this interface,
/// so tests use a fake and CI never calls Azure.
/// </summary>
public interface ILlmClient
{
    Task<LlmResult> CompleteJsonAsync(string systemPrompt, string userPrompt, string schemaName, string jsonSchema, CancellationToken cancellationToken);
}
