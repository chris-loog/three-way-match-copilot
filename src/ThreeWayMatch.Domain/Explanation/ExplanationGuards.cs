using System.Text.RegularExpressions;

namespace ThreeWayMatch.Domain.Explanation;

/// <summary>
/// Checks the model's answer against the facts it was given. An answer that fails is retried once, then replaced
/// by the template. The guards are plain code: they do not ask a model whether a model was right.
/// </summary>
public static partial class ExplanationGuards
{
    private const int MaxDraftLength = 1500;

    /// <summary>The reasons the answer cannot be shown. Empty means every guard passed.</summary>
    public static IReadOnlyList<string> Check(ExplanationFacts facts, ExplanationReply reply)
    {
        var failures = new List<string>();

        // Number guard: an amount or quantity the facts do not contain was made up.
        var unknownNumbers = NumberTokens.Unknown(reply.AllText, facts.Numbers());
        if (unknownNumbers.Count > 0)
        {
            failures.Add($"Numbers that are not in the facts: {string.Join(", ", unknownNumbers.Distinct())}.");
        }

        // Code guard: only discrepancy codes that exist in the facts.
        var known = facts.Findings.Select(f => f.Code).ToHashSet(StringComparer.Ordinal);
        var invented = CodeToken().Matches(reply.AllText).Select(m => m.Value).Where(c => !known.Contains(c)).Distinct().ToList();
        if (invented.Count > 0)
        {
            failures.Add($"Codes that are not in the facts: {string.Join(", ", invented)}.");
        }

        // Every finding that blocks payment must be named where the reviewer reads (not only in the supplier message).
        var reviewerText = string.Join('\n', reply.Summary, reply.LikelyCause, reply.SuggestedAction);
        var missing = facts.Findings.Where(f => f.Severity == "Block" && !reviewerText.Contains(f.Code, StringComparison.Ordinal)).Select(f => f.Code).Distinct().ToList();
        if (missing.Count > 0)
        {
            failures.Add($"Blocking findings not mentioned: {string.Join(", ", missing)}.");
        }

        // The program writes the greeting, closing and name. A bracket in the body is a placeholder the model left behind.
        if (reply.DraftMessageToSupplier.AsSpan().IndexOfAny('[', ']') >= 0)
        {
            failures.Add("The message to the supplier contains a placeholder in square brackets.");
        }

        if (reply.DraftMessageToSupplier.Length > MaxDraftLength)
        {
            failures.Add("The message to the supplier is too long.");
        }

        return failures;
    }

    [GeneratedRegex(@"\b[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+\b")]
    private static partial Regex CodeToken();
}
