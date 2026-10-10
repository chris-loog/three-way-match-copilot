using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Rules;
using ThreeWayMatch.Domain.Tests.Matching;
using static ThreeWayMatch.Domain.Tests.Rules.RuleTestSupport;

namespace ThreeWayMatch.Domain.Tests.Rules;

public class PriceRulesTests
{
    private readonly MatchingWorld _world = new();

    [Theory]
    [InlineData(100, false)]
    [InlineData(101, false)] // 1 % of 100
    [InlineData(101.5, true)]
    [InlineData(110, true)]
    public void A_price_above_the_order_price_beyond_the_tolerance_is_reported(double price, bool expected)
    {
        var assessment = Assess(TestInvoice.Board(10m, (decimal)price), _world);

        Assert.Equal(expected, assessment.Codes().Contains(DiscrepancyCode.PriceAbovePo));
    }

    [Fact]
    public void The_tolerance_is_capped_in_euros_however_expensive_the_item()
    {
        foreach (var line in _world.Po1.Lines.Where(l => l.SupplierItem == _world.Board))
        {
            line.UnitPrice = 1000m; // 1 % would be 10, the cap is 5
        }

        Assert.DoesNotContain(DiscrepancyCode.PriceAbovePo, Assess(TestInvoice.Board(1m, 1005m), _world).Codes());
        Assert.Contains(DiscrepancyCode.PriceAbovePo, Assess(TestInvoice.Board(1m, 1006m), _world).Codes());
    }

    [Fact]
    public void A_supplier_can_have_its_own_price_tolerance()
    {
        _world.Timber.PriceTolerancePercent = 3m;

        Assert.DoesNotContain(DiscrepancyCode.PriceAbovePo, Assess(TestInvoice.Board(1m, 103m), _world).Codes());
        Assert.Contains(DiscrepancyCode.PriceAbovePo, Assess(TestInvoice.Board(1m, 104m), _world).Codes());
    }

    [Theory]
    [InlineData(10, 120, Severity.Warning)] // 200 too much
    [InlineData(10, 150, Severity.Warning)] // 500, not above the threshold
    [InlineData(10, 151, Severity.Block)] // 510
    public void An_overcharge_becomes_a_block_above_the_euro_threshold(int quantity, int price, Severity expected)
    {
        var finding = Assess(TestInvoice.Board(quantity, price), _world).Single(DiscrepancyCode.PriceAbovePo);

        Assert.Equal(expected, finding.Severity);
        Assert.Equal((price - 100m) * quantity, finding.AmountAtStake);
    }

    [Fact]
    public void A_price_below_the_order_price_is_only_information_and_does_not_stop_approval()
    {
        var assessment = Assess(TestInvoice.Board(10m, 90m), _world);

        var finding = assessment.Single(DiscrepancyCode.PriceBelowPo);
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal(0m, finding.AmountAtStake);
        Assert.Equal(Decision.AutoApproved, assessment.Decision);
        Assert.Equal(0m, assessment.AmountAtStake);
    }

    [Fact]
    public void A_price_per_base_unit_is_compared_with_the_order_price_per_pack()
    {
        // The order says 100 per pack of 0.25 m³, so 400 per m³ is the same price.
        Assert.Empty(Assess(new TestInvoice().Line("KS-0102", "Kuuseliist", 2m, "m³", 400m), _world).Findings);
        Assert.Contains(DiscrepancyCode.PriceAbovePo, Assess(new TestInvoice().Line("KS-0102", "Kuuseliist", 2m, "m³", 450m), _world).Codes());
    }

    [Fact]
    public void A_price_in_an_unconvertible_unit_is_not_compared()
    {
        var assessment = Assess(new TestInvoice().Line("KS-0101", "Kuuselaud", 10m, "rm", 500m), _world);

        Assert.DoesNotContain(DiscrepancyCode.PriceAbovePo, assessment.Codes());
        Assert.DoesNotContain(DiscrepancyCode.PriceBelowPo, assessment.Codes());
    }
}
