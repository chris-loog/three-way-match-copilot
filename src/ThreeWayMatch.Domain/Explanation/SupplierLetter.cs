namespace ThreeWayMatch.Domain.Explanation;

/// <summary>
/// Builds the final answer from a checked one. The parts that must be exact are written here, not by the model:
/// the greeting with the supplier's name, the closing with our company, and the fixed instruction for the codes that need one.
/// </summary>
public static class SupplierLetter
{
    public const string DefaultCompany = "Näidis Puit OÜ";

    /// <summary>
    /// Takes a reply whose draft is only a body (from the model or from the template) and returns the reply the reviewer sees:
    /// the fixed instructions are appended to the next step, and the draft is either framed as a letter or, when the supplier must
    /// not be written to, empty.
    /// </summary>
    public static ExplanationReply Finish(ExplanationFacts facts, ExplanationReply reply, string company = DefaultCompany)
    {
        var instructions = facts.Findings
            .Select(f => FindingCatalogue.ReviewerInstruction(f.Code))
            .Where(text => text is not null)
            .Distinct(StringComparer.Ordinal);
        var action = string.Join(' ', new[] { reply.SuggestedAction }.Concat(instructions!));

        var draft = facts.SupplierDraftAllowed && !string.IsNullOrWhiteSpace(reply.DraftMessageToSupplier)
            ? Frame(facts, reply.DraftMessageToSupplier.Trim(), company)
            : string.Empty;

        return reply with { SuggestedAction = action, DraftMessageToSupplier = draft };
    }

    private static string Frame(ExplanationFacts facts, string body, string company)
    {
        var supplier = facts.Supplier;
        return facts.SupplierLanguage.Equals("Estonian", StringComparison.OrdinalIgnoreCase)
            ? $"{(supplier is null ? "Lugupeetud koostööpartner" : $"Lugupeetud {supplier}")},\n\n{body}\n\nLugupidamisega,\n{company} ostuosakond"
            : $"{(supplier is null ? "Dear Sir or Madam" : $"Dear {supplier}")},\n\n{body}\n\nKind regards,\n{company} purchasing";
    }
}
