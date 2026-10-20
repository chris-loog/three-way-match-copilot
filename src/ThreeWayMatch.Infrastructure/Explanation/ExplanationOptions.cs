using ThreeWayMatch.Domain.Explanation;

namespace ThreeWayMatch.Infrastructure.Explanation;

public sealed class ExplanationOptions
{
    /// <summary>For example <c>https://aoai-three-way-match.openai.azure.com/</c>. Null: no model is configured and the template text is used.</summary>
    public Uri? Endpoint { get; init; }

    /// <summary>The name of the model deployment in Azure OpenAI.</summary>
    public string Deployment { get; init; } = "gpt-4.1-mini";

    /// <summary>Our company name in the closing of a message to a supplier.</summary>
    public string CompanyName { get; init; } = SupplierLetter.DefaultCompany;

    public float Temperature { get; init; } = 0.2f;

    public int MaxOutputTokens { get; init; } = 900;
}
