using System.Net;

namespace ThreeWayMatch.Domain.Tests.Web;

public partial class PageTests
{
    [Fact]
    public async Task A_document_waiting_for_a_decision_offers_to_explain_it()
    {
        using var world = await WorldWithOverBilledInvoice();
        var id = await world.IdOfAsync("b.pdf");

        var html = await world.CreateClient().GetStringAsync($"/documents/{id}");

        Assert.Contains("Why it was stopped", html, StringComparison.Ordinal);
        Assert.Contains("Explain this", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_automatically_approved_document_has_no_explanation_panel()
    {
        using var world = await WorldWithOverBilledInvoice();
        var id = await world.IdOfAsync("a.pdf");

        Assert.DoesNotContain("Why it was stopped", await world.CreateClient().GetStringAsync($"/documents/{id}"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_model_the_explain_button_shows_the_standard_text_built_from_the_findings()
    {
        using var world = await WorldWithOverBilledInvoice();
        var id = await world.IdOfAsync("b.pdf");
        var client = world.CreateClient(new() { AllowAutoRedirect = false });
        var token = await TokenFrom(client, $"/documents/{id}");

        var response = await client.PostAsync($"/documents/{id}?handler=Explain", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var html = await client.GetStringAsync($"/documents/{id}");
        Assert.Contains("QTY_OVER_INVOICED", html, StringComparison.Ordinal);
        Assert.Contains("Standard text built from the findings", html, StringComparison.Ordinal);
        Assert.Contains("Draft message to the supplier", html, StringComparison.Ordinal);
        Assert.Contains("Explain again", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explaining_over_and_over_from_one_address_is_throttled()
    {
        using var world = await WorldWithOverBilledInvoice();
        var id = await world.IdOfAsync("b.pdf");
        var client = world.CreateClient(new() { AllowAutoRedirect = false });
        var token = await TokenFrom(client, $"/documents/{id}");
        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = token };

        for (var i = 0; i < 11; i++)
        {
            await client.PostAsync($"/documents/{id}?handler=Explain", new FormUrlEncodedContent(form));
        }

        Assert.Contains("Too many explanation requests", await client.GetStringAsync($"/documents/{id}"), StringComparison.Ordinal);
    }
}
