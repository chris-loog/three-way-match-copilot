namespace ThreeWayMatch.Domain.Ap;

public enum ExplanationSource
{
    /// <summary>Written by the model and passed every guard.</summary>
    Model,

    /// <summary>Built from the facts with fixed wording: the model was unavailable or failed the guards twice.</summary>
    Template,
}

/// <summary>The stored explanation of why a document was stopped. One per document; a new match removes it.</summary>
public class MatchExplanation
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public ExplanationSource Source { get; set; }
    public string? ModelId { get; set; }
    public required string Summary { get; set; }
    public required string LikelyCause { get; set; }
    public required string SuggestedAction { get; set; }
    public required string DraftMessage { get; set; }

    /// <summary>The language of the draft message, e.g. <c>Estonian</c>.</summary>
    public required string DraftLanguage { get; set; }

    /// <summary>Why the model's answer was not used (guard failures or an error); null when it was.</summary>
    public string? GuardNotes { get; set; }

    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }

    public Document? Document { get; set; }
}
