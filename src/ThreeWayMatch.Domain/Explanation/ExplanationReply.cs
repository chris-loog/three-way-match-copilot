using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThreeWayMatch.Domain.Explanation;

/// <summary>
/// The model's answer. It explains and suggests; it never approves or rejects anything. <see cref="DraftMessageToSupplier"/> is the
/// body of a letter only: the greeting, the closing and the supplier's name are added in C# (<see cref="SupplierLetter"/>).
/// It is empty when the document must not be written to the supplier about.
/// </summary>
public sealed record ExplanationReply(
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("likely_cause")] string LikelyCause,
    [property: JsonPropertyName("suggested_action")] string SuggestedAction,
    [property: JsonPropertyName("draft_message_to_supplier")] string DraftMessageToSupplier)
{
    public const string SchemaName = "invoice_explanation";

    /// <summary>The JSON schema the model must follow (structured output).</summary>
    public const string Schema = """
        {
          "type": "object",
          "properties": {
            "summary": { "type": "string", "description": "Two or three sentences for the reviewer: what is wrong and how much money it concerns." },
            "likely_cause": { "type": "string", "description": "The most likely reason, based only on the facts. Say so when it is unclear." },
            "suggested_action": { "type": "string", "description": "What the reviewer should check or do next. A suggestion, not a decision." },
            "draft_message_to_supplier": { "type": "string", "description": "The body of a short, polite message to the supplier, in the language given in the facts. No greeting, no closing, no name, no placeholders." }
          },
          "required": ["summary", "likely_cause", "suggested_action", "draft_message_to_supplier"],
          "additionalProperties": false
        }
        """;

    /// <summary>The same schema without the draft: used when no message to the supplier is allowed, so the model cannot write one.</summary>
    public const string SchemaWithoutDraft = """
        {
          "type": "object",
          "properties": {
            "summary": { "type": "string", "description": "Two or three sentences for the reviewer: what is wrong and how much money it concerns." },
            "likely_cause": { "type": "string", "description": "The most likely reason, based only on the facts. Say so when it is unclear." },
            "suggested_action": { "type": "string", "description": "What the reviewer should check or do next. A suggestion, not a decision." }
          },
          "required": ["summary", "likely_cause", "suggested_action"],
          "additionalProperties": false
        }
        """;

    public static string SchemaFor(bool withDraft) => withDraft ? Schema : SchemaWithoutDraft;

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Parses the model's text. Null when it is not valid JSON of this shape or a field is empty. With <paramref name="draftExpected"/> false
    /// the draft is not required, and any draft the model wrote anyway is dropped.
    /// </summary>
    public static ExplanationReply? TryParse(string text, bool draftExpected = true)
    {
        try
        {
            var reply = JsonSerializer.Deserialize<ExplanationReply>(text, Options);
            if (reply is null || string.IsNullOrWhiteSpace(reply.Summary) || string.IsNullOrWhiteSpace(reply.LikelyCause) || string.IsNullOrWhiteSpace(reply.SuggestedAction))
            {
                return null;
            }

            return draftExpected
                ? string.IsNullOrWhiteSpace(reply.DraftMessageToSupplier) ? null : reply
                : reply with { DraftMessageToSupplier = string.Empty };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string AllText => string.Join('\n', Summary, LikelyCause, SuggestedAction, DraftMessageToSupplier);
}
