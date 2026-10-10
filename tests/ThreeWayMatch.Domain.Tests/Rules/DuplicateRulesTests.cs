using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Rules;
using ThreeWayMatch.Domain.Tests.Matching;
using static ThreeWayMatch.Domain.Tests.Rules.RuleTestSupport;

namespace ThreeWayMatch.Domain.Tests.Rules;

public class DuplicateRulesTests
{
    private readonly MatchingWorld _world = new();

    private static ProcessedInvoice Earlier(string? hash = "hash-old", string number = "A-0", decimal gross = 1240m, int day = 2, int supplierId = 1) =>
        new(hash, supplierId, number, gross, new DateOnly(2026, 4, day));

    [Fact]
    public void The_same_file_is_a_duplicate_whatever_the_invoice_says()
    {
        var assessment = Assess(TestInvoice.Board(), _world, hash: "hash-old", processed: [Earlier()]);

        var finding = assessment.Single(DiscrepancyCode.DuplicateInvoice);
        Assert.Equal(Severity.Block, finding.Severity);
        Assert.Equal(Decision.Rejected, assessment.Decision);
    }

    [Theory]
    [InlineData("A-1")]
    [InlineData("a 1")]
    [InlineData("A1")]
    public void The_same_invoice_number_from_the_same_supplier_is_a_duplicate_even_in_a_different_file(string earlierNumber)
    {
        var assessment = Assess(TestInvoice.Board(), _world, hash: "hash-different", processed: [Earlier(hash: "hash-old", number: earlierNumber)]);

        Assert.Contains(DiscrepancyCode.DuplicateInvoice, assessment.Codes());
        Assert.Equal(Decision.Rejected, assessment.Decision);
    }

    [Fact]
    public void The_same_invoice_number_from_another_supplier_is_not_a_duplicate()
    {
        var assessment = Assess(TestInvoice.Board(), _world, processed: [Earlier(number: "A-1", supplierId: 2)]);

        Assert.DoesNotContain(DiscrepancyCode.DuplicateInvoice, assessment.Codes());
    }

    [Fact]
    public void An_invoice_with_no_earlier_invoices_is_not_a_duplicate()
    {
        Assert.DoesNotContain(DiscrepancyCode.DuplicateInvoice, Assess(TestInvoice.Board(), _world).Codes());
    }

    [Fact]
    public void Same_supplier_same_amount_a_few_days_apart_under_a_new_number_is_a_possible_duplicate()
    {
        var assessment = Assess(TestInvoice.Board(), _world, processed: [Earlier(number: "A-0", day: 1)]);

        var finding = assessment.Single(DiscrepancyCode.PossibleDuplicate);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("A-0", finding.Expected);
        Assert.Equal(Decision.NeedsReview, assessment.Decision);
    }

    [Fact]
    public void A_possible_duplicate_is_never_rejected_automatically()
    {
        Assert.NotEqual(Decision.Rejected, Assess(TestInvoice.Board(), _world, processed: [Earlier(number: "A-0")]).Decision);
    }

    [Theory]
    [InlineData(1240.0, 16, true)] // exactly 14 days before April 30: inside the window
    [InlineData(1240.0, 15, false)] // 15 days: outside
    [InlineData(1240.0, 30, true)] // the same day under a different number
    [InlineData(1500.0, 16, false)] // a different amount
    [InlineData(1240.004, 16, true)] // the same amount to the cent
    public void A_possible_duplicate_needs_the_same_amount_and_a_date_within_the_window(double gross, int earlierDay, bool expected)
    {
        var invoice = TestInvoice.Board();
        invoice.Date = new DateOnly(2026, 4, 30);
        var earlier = new ProcessedInvoice("hash-old", 1, "A-0", (decimal)gross, new DateOnly(2026, 4, earlierDay));

        var assessment = Assess(invoice, _world, processed: [earlier]);

        Assert.Equal(expected, assessment.Codes().Contains(DiscrepancyCode.PossibleDuplicate));
    }

    [Fact]
    public void Two_invoices_for_different_orders_with_the_same_amount_are_not_suspect()
    {
        // Standard services cost the same every time. PO-2026-0001 is order id 1; the earlier invoice was for order 2.
        var earlier = new ProcessedInvoice("hash-old", 1, "A-0", 1240m, new DateOnly(2026, 4, 1), PurchaseOrderId: 2);

        Assert.DoesNotContain(DiscrepancyCode.PossibleDuplicate, Assess(TestInvoice.Board(), _world, processed: [earlier]).Codes());
    }

    [Fact]
    public void The_same_order_or_an_unknown_order_keeps_the_doubt()
    {
        var sameOrder = new ProcessedInvoice("hash-old", 1, "A-0", 1240m, new DateOnly(2026, 4, 1), PurchaseOrderId: 1);
        var unknownOrder = new ProcessedInvoice("hash-old", 1, "A-0", 1240m, new DateOnly(2026, 4, 1));

        Assert.Contains(DiscrepancyCode.PossibleDuplicate, Assess(TestInvoice.Board(), _world, processed: [sameOrder]).Codes());
        Assert.Contains(DiscrepancyCode.PossibleDuplicate, Assess(TestInvoice.Board(), _world, processed: [unknownOrder]).Codes());
    }

    [Fact]
    public void Dates_more_than_the_window_apart_are_not_a_possible_duplicate()
    {
        var invoice = TestInvoice.Board();
        invoice.Date = new DateOnly(2026, 5, 20);

        var assessment = Assess(invoice, _world, processed: [Earlier(number: "A-0", day: 2)]);

        Assert.DoesNotContain(DiscrepancyCode.PossibleDuplicate, assessment.Codes());
    }

    [Fact]
    public void An_exact_resend_is_one_finding_not_two()
    {
        // Same number and same file: DUPLICATE_INVOICE only, not also POSSIBLE_DUPLICATE.
        var assessment = Assess(TestInvoice.Board(), _world, hash: "hash-old", processed: [Earlier(number: "A-1")]);

        Assert.Equal([DiscrepancyCode.DuplicateInvoice], assessment.Codes());
    }
}
