using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Domain.Rules;
using ThreeWayMatch.Domain.Tests.Matching;
using static ThreeWayMatch.Domain.Tests.Rules.RuleTestSupport;

namespace ThreeWayMatch.Domain.Tests.Rules;

public class TotalsAndBankTests
{
    private readonly MatchingWorld _world = new();

    // ---------- TOTALS_MISMATCH ----------

    [Fact]
    public void Totals_that_add_up_report_nothing()
    {
        Assert.DoesNotContain(DiscrepancyCode.TotalsMismatch, Assess(TestInvoice.Board(), _world).Codes());
    }

    [Fact]
    public void Lines_that_do_not_add_up_to_the_net_total_are_reported()
    {
        var invoice = TestInvoice.Board();
        invoice.NetOverride = 1010m; // the line says 1000

        var finding = Assess(invoice, _world).Single(DiscrepancyCode.TotalsMismatch);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(10m, finding.AmountAtStake);
        Assert.Contains("net total", finding.Actual);
    }

    [Fact]
    public void A_vat_total_that_is_not_24_percent_of_the_lines_is_reported()
    {
        var invoice = TestInvoice.Board();
        invoice.VatOverride = 250m; // 24 % of 1000 is 240

        var finding = Assess(invoice, _world).Single(DiscrepancyCode.TotalsMismatch);

        Assert.Equal(10m, finding.AmountAtStake);
        Assert.Contains("VAT", finding.Actual);
    }

    [Fact]
    public void A_gross_total_that_is_not_net_plus_vat_is_reported()
    {
        var invoice = TestInvoice.Board();
        invoice.GrossOverride = 1260m; // 1000 + 240 = 1240

        var finding = Assess(invoice, _world).Single(DiscrepancyCode.TotalsMismatch);

        Assert.Equal(20m, finding.AmountAtStake);
        Assert.Contains("gross", finding.Actual);
    }

    [Theory]
    [InlineData(0.04, false)]
    [InlineData(0.05, false)]
    [InlineData(0.06, true)]
    public void A_difference_within_the_rounding_tolerance_is_not_a_mismatch(double difference, bool expected)
    {
        var invoice = TestInvoice.Board();
        invoice.GrossOverride = 1240m + (decimal)difference;

        Assert.Equal(expected, Assess(invoice, _world).Codes().Contains(DiscrepancyCode.TotalsMismatch));
    }

    [Fact]
    public void A_total_that_was_not_found_is_not_compared_but_is_unreliable()
    {
        var invoice = TestInvoice.Board().Build() with { GrossTotal = ExtractedField.Missing<decimal?>() };

        var assessment = Assess(invoice, _world);

        Assert.DoesNotContain(DiscrepancyCode.TotalsMismatch, assessment.Codes());
        Assert.Contains(DiscrepancyCode.LowConfidence, assessment.Codes());
    }

    // ---------- BANK_ACCOUNT_CHANGED ----------

    [Theory]
    [InlineData("EE78 0400 1503 1113 4947")]
    [InlineData("EE780400150311134947")]
    [InlineData("ee78 0400 1503 1113 4947")]
    public void The_suppliers_own_iban_is_not_a_change_however_it_is_spaced(string printed)
    {
        var invoice = TestInvoice.Board();
        invoice.Iban = printed;

        Assert.DoesNotContain(DiscrepancyCode.BankAccountChanged, Assess(invoice, _world).Codes());
    }

    [Fact]
    public void An_iban_with_a_wrong_checksum_is_a_probable_misread_not_a_changed_account()
    {
        var invoice = TestInvoice.Board();
        invoice.Iban = "EE78 0400 1503 1113 4948"; // last digit changed

        var assessment = Assess(invoice, _world);

        Assert.DoesNotContain(DiscrepancyCode.BankAccountChanged, assessment.Codes());
        Assert.Contains(DiscrepancyCode.LowConfidence, assessment.Codes());
        Assert.Equal(Decision.NeedsReview, assessment.Decision);
    }

    [Fact]
    public void A_missing_iban_is_not_a_change_but_is_unreliable()
    {
        var invoice = TestInvoice.Board().Build() with { Iban = ExtractedField.Missing<string>() };

        var assessment = Assess(invoice, _world);

        Assert.DoesNotContain(DiscrepancyCode.BankAccountChanged, assessment.Codes());
        Assert.Contains(DiscrepancyCode.LowConfidence, assessment.Codes());
    }

    [Fact]
    public void A_bank_change_is_a_block_and_carries_the_invoice_value_as_amount_at_stake()
    {
        var invoice = TestInvoice.Board();
        invoice.Iban = _world.Panels.Iban;

        var finding = Assess(invoice, _world).Single(DiscrepancyCode.BankAccountChanged);

        Assert.Equal(Severity.Block, finding.Severity);
        Assert.Equal(1240m, finding.AmountAtStake);
    }
}
