using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ThreeWayMatch.Web.Services;

namespace ThreeWayMatch.Web.Pages;

public sealed class IndexModel(ReviewQueries queries, DemoData demoData, DemoMode demo, TimeProvider clock) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public InboxFilter Show { get; set; } = InboxFilter.NeedsReview;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public IReadOnlyList<InboxRow> Rows { get; private set; } = [];

    public DateTimeOffset Now => clock.GetUtcNow();

    public decimal TotalAtStake => Rows.Sum(r => r.AmountAtStake);

    public string Heading => Show switch
    {
        InboxFilter.OnHold => "On hold",
        InboxFilter.AutoApproved => "Approved automatically",
        InboxFilter.Rejected => "Rejected",
        InboxFilter.Reviewed => "Approved by reviewer",
        _ => "Needs review",
    };

    public string Lede => Show switch
    {
        InboxFilter.OnHold => "These invoices arrived before the goods were booked. They are checked again every morning, and move on by themselves once the goods are received.",
        InboxFilter.AutoApproved => "Nothing was wrong with these. They were approved without anyone touching them.",
        InboxFilter.Rejected => "These are exact copies of an invoice we already have.",
        InboxFilter.Reviewed => "A reviewer looked at these and approved them. The reason is kept with each one.",
        _ => "The most money at risk comes first. Open an invoice to compare it with the order and the delivery.",
    };

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var rows = await queries.InboxAsync(Show, cancellationToken);
        Rows = string.IsNullOrWhiteSpace(Search)
            ? rows
            : rows.Where(r => r.Supplier.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)
                || (r.Number?.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.PurchaseOrder?.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
    }

    public async Task<IActionResult> OnPostResetDemoAsync(CancellationToken cancellationToken)
    {
        if (!demo.Enabled)
        {
            return NotFound();
        }

        await demoData.ResetAsync(cancellationToken);
        TempData["Flash"] = "Demo data cleared. Upload a document to start again.";
        return RedirectToPage();
    }
}
