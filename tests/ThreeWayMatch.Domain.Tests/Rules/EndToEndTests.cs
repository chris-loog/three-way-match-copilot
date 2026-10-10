using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Rules;
using ThreeWayMatch.Domain.Tests.Matching;
using static ThreeWayMatch.Domain.Tests.Rules.RuleTestSupport;

namespace ThreeWayMatch.Domain.Tests.Rules;

/// <summary>Five whole documents through match, cross-check, every rule and the decision.</summary>
public class EndToEndTests
{
    private readonly MatchingWorld _world = new();

    [Fact]
    public void A_clean_invoice_is_approved_automatically_with_nothing_to_report()
    {
        var assessment = Assess(TestInvoice.Board(), _world);

        Assert.Equal(Decision.AutoApproved, assessment.Decision);
        Assert.Empty(assessment.Findings);
        Assert.Empty(assessment.Concerns);
        Assert.Equal(0m, assessment.AmountAtStake);
    }

    [Fact]
    public void An_overcharge_above_the_euro_threshold_is_a_block_and_goes_to_review()
    {
        // 10 m³ at 160 instead of 100: 600 euro too much, above the 500 threshold.
        var assessment = Assess(TestInvoice.Board(10m, 160m), _world);

        Assert.Equal(Decision.NeedsReview, assessment.Decision);
        var finding = assessment.Single(DiscrepancyCode.PriceAbovePo);
        Assert.Equal(Severity.Block, finding.Severity);
        Assert.Equal(600m, finding.AmountAtStake);
        Assert.Equal(600m, assessment.AmountAtStake);
    }

    [Fact]
    public void An_invoice_that_arrives_before_the_goods_waits_instead_of_being_rejected()
    {
        // 20 packs of lath invoiced; only 8 of the 20 have arrived, and the order is for 20.
        var invoice = new TestInvoice().Line("KS-0102", "Kuuseliist 22x45 mm", 20m, "pakk", 100m);

        var assessment = Assess(invoice, _world);

        Assert.Equal(Decision.OnHold, assessment.Decision);
        Assert.Equal([DiscrepancyCode.PartialDelivery], assessment.Codes());
    }

    [Fact]
    public void The_same_file_again_is_rejected()
    {
        var earlier = new ProcessedInvoice("hash-first", _world.Timber.Id, "A-1", 1240m, new DateOnly(2026, 4, 2));

        var assessment = Assess(TestInvoice.Board(), _world, hash: "hash-first", processed: [earlier]);

        Assert.Equal(Decision.Rejected, assessment.Decision);
        Assert.Contains(DiscrepancyCode.DuplicateInvoice, assessment.Codes());
    }

    [Fact]
    public void A_changed_bank_account_is_a_block_even_when_everything_else_is_perfect()
    {
        var invoice = TestInvoice.Board();
        invoice.Iban = _world.Panels.Iban; // a perfectly valid IBAN, just not this supplier's

        var assessment = Assess(invoice, _world);

        Assert.Equal(Decision.NeedsReview, assessment.Decision);
        var finding = assessment.Single(DiscrepancyCode.BankAccountChanged);
        Assert.Equal(Severity.Block, finding.Severity);
        Assert.Equal(_world.Timber.Iban, finding.Expected);
        Assert.Equal(_world.Panels.Iban, finding.Actual);
    }
}
