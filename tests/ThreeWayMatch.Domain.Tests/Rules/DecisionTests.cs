using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Matching;
using ThreeWayMatch.Domain.Rules;

namespace ThreeWayMatch.Domain.Tests.Rules;

public class DecisionTests
{
    private static Finding F(DiscrepancyCode code, Severity severity, decimal amount = 0m) => new(code, severity, null, null, null, amount);

    private static readonly MatchConcern Guess = new(ConcernKind.FuzzyLineMatch, 1, "guess");

    [Fact]
    public void Nothing_found_is_approved()
    {
        Assert.Equal(Decision.AutoApproved, DecisionMaker.Decide([], []));
    }

    [Fact]
    public void Information_alone_does_not_stop_approval()
    {
        Assert.Equal(Decision.AutoApproved, DecisionMaker.Decide([F(DiscrepancyCode.PriceBelowPo, Severity.Info)], []));
    }

    [Theory]
    [InlineData(Severity.Warning)]
    [InlineData(Severity.Block)]
    public void Any_warning_or_block_needs_a_person(Severity severity)
    {
        Assert.Equal(Decision.NeedsReview, DecisionMaker.Decide([F(DiscrepancyCode.TotalsMismatch, severity)], []));
    }

    [Fact]
    public void A_guess_by_the_matcher_needs_a_person_even_with_no_findings()
    {
        Assert.Equal(Decision.NeedsReview, DecisionMaker.Decide([], [Guess]));
    }

    [Fact]
    public void Waiting_for_goods_alone_puts_the_document_on_hold()
    {
        Assert.Equal(Decision.OnHold, DecisionMaker.Decide([F(DiscrepancyCode.PartialDelivery, Severity.Warning)], []));
        Assert.Equal(Decision.OnHold, DecisionMaker.Decide([F(DiscrepancyCode.PartialDelivery, Severity.Warning), F(DiscrepancyCode.PriceBelowPo, Severity.Info)], []));
    }

    [Fact]
    public void Waiting_does_not_fix_a_second_problem_so_the_document_goes_to_a_person_instead()
    {
        Assert.Equal(Decision.NeedsReview,
            DecisionMaker.Decide([F(DiscrepancyCode.PartialDelivery, Severity.Warning), F(DiscrepancyCode.BankAccountChanged, Severity.Block)], []));
        Assert.Equal(Decision.NeedsReview, DecisionMaker.Decide([F(DiscrepancyCode.PartialDelivery, Severity.Warning)], [Guess]));
    }

    [Fact]
    public void An_exact_duplicate_is_rejected_whatever_else_is_found()
    {
        Assert.Equal(Decision.Rejected,
            DecisionMaker.Decide([F(DiscrepancyCode.DuplicateInvoice, Severity.Block), F(DiscrepancyCode.PriceAbovePo, Severity.Block)], [Guess]));
    }

    [Fact]
    public void A_possible_duplicate_is_never_rejected()
    {
        Assert.Equal(Decision.NeedsReview, DecisionMaker.Decide([F(DiscrepancyCode.PossibleDuplicate, Severity.Warning)], []));
    }

    [Fact]
    public void Amount_at_stake_adds_up_money_at_risk_and_ignores_information()
    {
        var findings = new[]
        {
            F(DiscrepancyCode.PriceAbovePo, Severity.Block, 600m),
            F(DiscrepancyCode.TotalsMismatch, Severity.Warning, 12.5m),
            F(DiscrepancyCode.PriceBelowPo, Severity.Info, 900m),
        };

        Assert.Equal(612.5m, DecisionMaker.AmountAtStake(findings));
    }
}
