using Microsoft.AspNetCore.Mvc;
using ThreeWayMatch.Web.Services;

namespace ThreeWayMatch.Web.ViewComponents;

/// <summary>The left rail: where to go, and how many documents wait in each place.</summary>
public sealed class RailViewComponent(ReviewQueries queries, DemoMode demo) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync() =>
        View(new RailModel(await queries.CountsAsync(HttpContext.RequestAborted), demo.Enabled, UserClaimsPrincipal.Identity?.Name ?? "Reviewer"));
}

public sealed record RailModel(InboxCounts Counts, bool Demo, string User);
