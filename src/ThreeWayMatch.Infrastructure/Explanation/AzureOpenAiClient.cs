using Azure.AI.OpenAI;
using OpenAI.Chat;
using ThreeWayMatch.Domain.Explanation;

namespace ThreeWayMatch.Infrastructure.Explanation;

/// <summary>The only class that calls Azure OpenAI. Low temperature and a JSON schema keep the answer stable and parseable.</summary>
internal sealed class AzureOpenAiClient(AzureOpenAIClient client, ExplanationOptions options) : ILlmClient
{
    public async Task<LlmResult> CompleteJsonAsync(string systemPrompt, string userPrompt, string schemaName, string jsonSchema, CancellationToken cancellationToken)
    {
        var chat = client.GetChatClient(options.Deployment);
        var completionOptions = new ChatCompletionOptions
        {
            Temperature = options.Temperature,
            MaxOutputTokenCount = options.MaxOutputTokens,
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(schemaName, BinaryData.FromString(jsonSchema), jsonSchemaIsStrict: true),
        };

        ChatCompletion completion = await chat.CompleteChatAsync(
            [new SystemChatMessage(systemPrompt), new UserChatMessage(userPrompt)], completionOptions, cancellationToken);

        var text = completion.Content.Count > 0 ? completion.Content[0].Text : string.Empty;
        return new LlmResult(text, completion.Usage.InputTokenCount, completion.Usage.OutputTokenCount, completion.Model);
    }
}
