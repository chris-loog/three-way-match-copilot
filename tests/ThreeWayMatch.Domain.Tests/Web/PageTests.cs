using System.Net;
using System.Text.RegularExpressions;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Tests.Pipeline;

namespace ThreeWayMatch.Domain.Tests.Web;

public partial class PageTests
{
    private static readonly byte[] PdfA = [1, 2, 3];
    private static readonly byte[] PdfB = [4, 5, 6];

    private static async Task<WebWorld> WorldWithOverBilledInvoice()
    {
        var world = new WebWorld();
        await world.ProcessAsync("incoming/invoices/a.pdf", PipelineWorld.CleanResult(), PdfA);
        await world.ProcessAsync("incoming/invoices/b.pdf", PipelineWorld.WithInvoiceNumber(PipelineWorld.CleanResult(), "BBS/2026/0999"), PdfB);
        return world;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex Token();

    private static async Task<string> TokenFrom(HttpClient client, string url) =>
        Token().Match(await client.GetStringAsync(url)).Groups[1].Value;

    [Fact]
    public async Task The_inbox_lists_the_invoice_that_needs_review_with_its_findings()
    {
        using var world = await WorldWithOverBilledInvoice();
        var html = await world.CreateClient().GetStringAsync("/");

        Assert.Contains("Baltic Board Supply", html, StringComparison.Ordinal);
        Assert.Contains("Billed more than received", html, StringComparison.Ordinal);
        Assert.Contains("BBS/2026/0999", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_rail_counts_what_waits_in_each_list()
    {
        using var world = await WorldWithOverBilledInvoice();
        var html = await world.CreateClient().GetStringAsync("/");

        Assert.Matches(@"Needs review\s*<span class=""count alert"">1</span>", html);
        Assert.Matches(@"Approved automatically\s*<span class=""count\s*"">1</span>", html);
    }

    [Fact]
    public async Task The_detail_page_compares_the_invoice_with_the_order_and_offers_approval()
    {
        using var world = await WorldWithOverBilledInvoice();
        var id = await world.IdOfAsync("b.pdf");

        var html = await world.CreateClient().GetStringAsync($"/documents/{id}");

        Assert.Contains("Needs a decision", html, StringComparison.Ordinal);
        Assert.Contains("class=\"tally\"", html, StringComparison.Ordinal);
        Assert.Contains("Approve this invoice", html, StringComparison.Ordinal);
        Assert.Contains("Spruce plywood", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_document_is_a_404()
    {
        using var world = new WebWorld();

        Assert.Equal(HttpStatusCode.NotFound, (await world.CreateClient().GetAsync("/documents/999")).StatusCode);
    }

    [Fact]
    public async Task Approving_through_the_form_marks_the_invoice_approved()
    {
        using var world = await WorldWithOverBilledInvoice();
        var id = await world.IdOfAsync("b.pdf");
        var client = world.CreateClient(new() { AllowAutoRedirect = false });
        var token = await TokenFrom(client, $"/documents/{id}");

        var response = await client.PostAsync($"/documents/{id}?handler=Approve", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["reason"] = "Second delivery confirmed with the warehouse.",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var page = await client.GetStringAsync($"/documents/{id}");
        Assert.Contains("Approved by Demo reviewer", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Approve this invoice", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_form_post_without_the_anti_forgery_token_is_refused()
    {
        using var world = await WorldWithOverBilledInvoice();
        var id = await world.IdOfAsync("b.pdf");

        var response = await world.CreateClient(new() { AllowAutoRedirect = false })
            .PostAsync($"/documents/{id}?handler=Approve", new FormUrlEncodedContent(new Dictionary<string, string> { ["reason"] = "Sneaky." }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_failed_page_lists_files_that_could_not_be_processed()
    {
        using var world = new WebWorld();
        await world.ProcessAsync("incoming/delivery-notes/dn-1.pdf", "{}", PdfA, DocumentType.DeliveryNote);

        var html = await world.CreateClient().GetStringAsync("/failed");

        Assert.Contains("dn-1.pdf", html, StringComparison.Ordinal);
        Assert.Contains("Delivery note", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_dashboard_shows_the_straight_through_rate()
    {
        using var world = await WorldWithOverBilledInvoice();

        var html = await world.CreateClient().GetStringAsync("/dashboard");

        Assert.Contains("50%", html, StringComparison.Ordinal);
        Assert.Contains("of 2 invoices were approved without anyone touching them", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_dashboard_is_usable_before_anything_is_processed()
    {
        using var world = new WebWorld();

        var response = await world.CreateClient().GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("No invoices have been processed yet", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Uploading_a_pdf_stores_it_as_an_invoice()
    {
        using var world = new WebWorld();
        var client = world.CreateClient(new() { AllowAutoRedirect = false });
        var token = await TokenFrom(client, "/upload");

        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent("Invoice"), "Type" },
            { new ByteArrayContent([1, 2, 3, 4]), "files", "supplier-invoice.pdf" },
        };
        var response = await client.PostAsync("/upload", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var upload = Assert.Single(world.Store.Uploads);
        Assert.Equal((DocumentType.Invoice, "supplier-invoice.pdf", 4), upload);
    }

    [Fact]
    public async Task Uploading_a_file_of_the_wrong_kind_is_refused_with_a_reason()
    {
        using var world = new WebWorld();
        var client = world.CreateClient(new() { AllowAutoRedirect = false });
        var token = await TokenFrom(client, "/upload");

        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent("Invoice"), "Type" },
            { new ByteArrayContent([1, 2, 3]), "files", "macro.exe" },
        };
        var response = await client.PostAsync("/upload", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("is not a PDF, JPG or PNG file", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(world.Store.Uploads);
    }
}
