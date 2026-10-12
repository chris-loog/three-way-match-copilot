using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Domain.Matching;

namespace ThreeWayMatch.Domain.Rules;

/// <summary>The final answer for one document, with everything needed to explain it.</summary>
public sealed record Assessment(
    Decision Decision,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<MatchConcern> Concerns,
    MatchOutcome Match,
    decimal AmountAtStake)
{
    /// <summary>The invoice after the cross-check (doubtful fields have confidence 0). Null for delivery notes.</summary>
    public ExtractedInvoice? CheckedInvoice { get; init; }
}

public static class DecisionMaker
{
    /// <summary>
    /// The decision, in order of precedence:
    /// Rejected for an exact duplicate; OnHold when the only real finding is that the goods have not all arrived;
    /// NeedsReview for any Warning, any Block, or any guess the match had to make; otherwise AutoApproved.
    /// A document that merely waits for goods is not held if something else is also wrong: waiting would not fix that.
    /// </summary>
    public static Decision Decide(IReadOnlyList<Finding> findings, IReadOnlyList<MatchConcern> concerns)
    {
        if (findings.Any(f => f.Code == DiscrepancyCode.DuplicateInvoice))
        {
            return Decision.Rejected;
        }

        var needsAttention = findings.Where(f => f.Severity >= Severity.Warning).ToList();
        if (needsAttention.Count > 0 && needsAttention.All(f => f.Code == DiscrepancyCode.PartialDelivery) && concerns.Count == 0)
        {
            return Decision.OnHold;
        }

        return needsAttention.Count > 0 || concerns.Count > 0 ? Decision.NeedsReview : Decision.AutoApproved;
    }

    /// <summary>Money at risk, for sorting the review queue. Info findings carry none.</summary>
    public static decimal AmountAtStake(IReadOnlyList<Finding> findings) =>
        findings.Where(f => f.Severity >= Severity.Warning).Sum(f => f.AmountAtStake);
}
