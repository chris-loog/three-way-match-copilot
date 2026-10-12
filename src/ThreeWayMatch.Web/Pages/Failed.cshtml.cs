using Microsoft.AspNetCore.Mvc.RazorPages;
using ThreeWayMatch.Web.Services;

namespace ThreeWayMatch.Web.Pages;

public sealed class FailedModel(ReviewQueries queries, TimeProvider clock) : PageModel
{
    public IReadOnlyList<FailedRow> Rows { get; private set; } = [];

    public DateTimeOffset Now => clock.GetUtcNow();

    public async Task OnGetAsync(CancellationToken cancellationToken) => Rows = await queries.FailedAsync(cancellationToken);
}
