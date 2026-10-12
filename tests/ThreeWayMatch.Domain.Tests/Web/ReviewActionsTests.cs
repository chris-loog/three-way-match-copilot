using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Tests.Pipeline;
using ThreeWayMatch.Web.Services;

namespace ThreeWayMatch.Domain.Tests.Web;

public class ReviewActionsTests
{
    private static readonly byte[] PdfA = [1, 2, 3];
    private static readonly byte[] PdfB = [4, 5, 6];

    /// <summary>Two invoices for the same goods: the second over-bills, so it needs review and has money at stake.</summary>
    private static async Task<(WebWorld World, int Id)> WorldWithOverBilledInvoice()
    {
        var world = new WebWorld();
        await world.ProcessAsync("incoming/invoices/a.pdf", PipelineWorld.CleanResult(), PdfA);
        await world.ProcessAsync("incoming/invoices/b.pdf", PipelineWorld.WithInvoiceNumber(PipelineWorld.CleanResult(), "BBS/2026/0999"), PdfB);
        return (world, await world.IdOfAsync("b.pdf"));
    }

    [Fact]
    public async Task Approving_needs_a_reason()
    {
        var (world, id) = await WorldWithOverBilledInvoice();
        using var _ = world;
        using var scope = world.Services.CreateScope();

        var result = await WebWorld.NewActions(scope).ApproveAsync(id, "Anna", "  ", false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("why", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Approving_records_who_when_and_why_and_moves_the_invoice_out_of_the_review_list()
    {
        var (world, id) = await WorldWithOverBilledInvoice();
        using var _ = world;
        using var scope = world.Services.CreateScope();

        var result = await WebWorld.NewActions(scope).ApproveAsync(id, "Anna", "Supplier confirmed the second delivery by phone.", false, CancellationToken.None);

        Assert.True(result.Succeeded);
        var queries = scope.ServiceProvider.GetRequiredService<ReviewQueries>();
        Assert.DoesNotContain(await queries.InboxAsync(InboxFilter.NeedsReview, CancellationToken.None), r => r.DocumentId == id);
        var reviewed = Assert.Single(await queries.InboxAsync(InboxFilter.Reviewed, CancellationToken.None));
        Assert.Equal(id, reviewed.DocumentId);
        var detail = (await queries.DetailAsync(id, CancellationToken.None))!;
        Assert.Equal("Anna", detail.ReviewedBy);
        Assert.Contains("second delivery", detail.OverrideReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_invoice_can_only_be_approved_once()
    {
        var (world, id) = await WorldWithOverBilledInvoice();
        using var _ = world;
        using var scope = world.Services.CreateScope();
        var actions = WebWorld.NewActions(scope);

        await actions.ApproveAsync(id, "Anna", "Checked with the warehouse.", false, CancellationToken.None);
        var again = await actions.ApproveAsync(id, "Mart", "Also fine.", false, CancellationToken.None);

        Assert.False(again.Succeeded);
    }

    [Fact]
    public async Task An_automatically_approved_invoice_cannot_be_approved_by_hand()
    {
        var (world, _) = await WorldWithOverBilledInvoice();
        using var _ = world;
        using var scope = world.Services.CreateScope();
        var first = await world.IdOfAsync("a.pdf");

        var result = await WebWorld.NewActions(scope).ApproveAsync(first, "Anna", "Looks fine.", false, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task A_changed_bank_account_needs_the_phone_check_before_approval()
    {
        using var world = new WebWorld();
        await world.WithDbAsync(async db =>
        {
            // The supplier master has another valid account than the one printed on the invoice.
            (await db.Suppliers.SingleAsync()).Iban = "EE382200221020145685";
            await db.SaveChangesAsync();
            return 0;
        });
        await world.ProcessAsync("incoming/invoices/a.pdf", PipelineWorld.CleanResult(), PdfA);
        var id = await world.IdOfAsync("a.pdf");
        using var scope = world.Services.CreateScope();
        var actions = WebWorld.NewActions(scope);

        var without = await actions.ApproveAsync(id, "Anna", "Looks fine.", false, CancellationToken.None);
        var with = await actions.ApproveAsync(id, "Anna", "Called the supplier, new account confirmed.", true, CancellationToken.None);

        Assert.False(without.Succeeded);
        Assert.Contains("bank", without.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(with.Succeeded);
        Assert.True((await scope.ServiceProvider.GetRequiredService<ReviewQueries>().DetailAsync(id, CancellationToken.None))!.BankChangeVerified);
    }

    [Fact]
    public async Task Correcting_a_doubtful_field_matches_the_invoice_again_and_keeps_a_record()
    {
        using var world = new WebWorld();
        var status = await world.ProcessAsync("incoming/invoices/a.pdf", PipelineWorld.SmudgedResult(), PdfA);
        Assert.Equal(DocumentStatus.NeedsReview, status);
        var id = await world.IdOfAsync("a.pdf");
        using var scope = world.Services.CreateScope();

        var result = await WebWorld.NewActions(scope).CorrectAsync(id, "Iban", "EE78 0400 1503 1113 4947", "Anna", CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        await world.WithDbAsync(async db =>
        {
            Assert.Equal(DocumentStatus.AutoApproved, (await db.Documents.SingleAsync()).Status);
            var correction = await db.FieldCorrections.SingleAsync();
            Assert.Equal("Iban", correction.FieldName);
            Assert.Equal(0.55, correction.ExtractedConfidence!.Value, precision: 2);
            Assert.Equal("Anna", correction.Reviewer);
            return 0;
        });
    }

    [Theory]
    [InlineData("GrossTotal", "twenty")]
    [InlineData("InvoiceDate", "12.04.2026")]
    [InlineData("Iban", "EE00 0000 0000 0000 0000")]
    [InlineData("Currency", "EUR")]
    [InlineData("InvoiceNumber", "")]
    public async Task A_correction_that_cannot_be_read_is_refused_and_nothing_is_saved(string field, string value)
    {
        using var world = new WebWorld();
        await world.ProcessAsync("incoming/invoices/a.pdf", PipelineWorld.SmudgedResult(), PdfA);
        var id = await world.IdOfAsync("a.pdf");
        using var scope = world.Services.CreateScope();

        var result = await WebWorld.NewActions(scope).CorrectAsync(id, field, value, "Anna", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, await world.WithDbAsync(db => db.FieldCorrections.CountAsync()));
    }

    [Fact]
    public async Task Confirming_a_wording_match_teaches_the_system_and_the_invoice_then_goes_through()
    {
        using var world = new WebWorld();
        // The supplier code on the first line is unknown, so only the wording can identify the item.
        var json = PipelineWorld.CleanResult().Replace("BBS00157", "ZZZ999", StringComparison.Ordinal);
        var status = await world.ProcessAsync("incoming/invoices/a.pdf", json, PdfA);
        Assert.Equal(DocumentStatus.NeedsReview, status);
        var id = await world.IdOfAsync("a.pdf");
        using var scope = world.Services.CreateScope();
        var detail = (await scope.ServiceProvider.GetRequiredService<ReviewQueries>().DetailAsync(id, CancellationToken.None))!;
        var guess = detail.Lines.Single(l => l.Method == MatchMethod.FuzzyDescription);

        var result = await WebWorld.NewActions(scope).ConfirmAliasAsync(id, guess.LineMatchId!.Value, "Anna", CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, await world.WithDbAsync(db => db.ItemAliases.CountAsync()));
        Assert.Equal(DocumentStatus.AutoApproved, await world.WithDbAsync(async db => (await db.Documents.SingleAsync()).Status));
    }

    [Fact]
    public async Task Resetting_the_demo_clears_what_the_app_made_but_keeps_the_ERP()
    {
        var (world, _) = await WorldWithOverBilledInvoice();
        using var _ = world;
        using var scope = world.Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<DemoData>().ResetAsync(CancellationToken.None);

        Assert.Equal(0, await world.WithDbAsync(db => db.Documents.CountAsync()));
        Assert.Equal(0, await world.WithDbAsync(db => db.MatchResults.CountAsync()));
        Assert.Equal(1, await world.WithDbAsync(db => db.PurchaseOrders.CountAsync()));
    }
}
