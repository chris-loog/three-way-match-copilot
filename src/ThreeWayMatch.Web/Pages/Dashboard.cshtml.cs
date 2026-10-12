using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ThreeWayMatch.Web.Services;

namespace ThreeWayMatch.Web.Pages;

public sealed class DashboardModel(ReviewQueries queries) : PageModel
{
    public DashboardStats Stats { get; private set; } = null!;

    /// <summary>The numbers the charts draw, as JSON for the page script.</summary>
    public string ChartData { get; private set; } = "{}";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Stats = await queries.DashboardAsync(cancellationToken);
        ChartData = JsonSerializer.Serialize(new
        {
            decisions = Stats.Decisions.Select(d => new { label = DecisionLabel(d.Decision), value = d.Count }),
            findings = Stats.TopFindings.Take(8).Select(f => new { label = Presentation.Title(f.Code), value = f.Count }),
        });
    }

    private static string DecisionLabel(string decision) => decision switch
    {
        "AutoApproved" => "Approved automatically",
        "OnHold" => "On hold",
        "NeedsReview" => "Needs review",
        "Rejected" => "Rejected",
        _ => decision,
    };
}
