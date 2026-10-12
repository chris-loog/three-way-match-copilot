using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ThreeWayMatch.Web.Services;

namespace ThreeWayMatch.Web.Pages.Documents;

public sealed partial class DetailModel(ReviewQueries queries, ReviewActions actions, IDocumentStore store, ILogger<DetailModel> logger) : PageModel
{
    public DocumentDetail Detail { get; private set; } = null!;

    public Uri? PreviewUrl { get; private set; }

    public bool PreviewIsImage => Detail.BlobPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || Detail.BlobPath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        || Detail.BlobPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

    private string Reviewer => User.Identity?.Name ?? "Unknown reviewer";

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var detail = await queries.DetailAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        Detail = detail;
        try
        {
            PreviewUrl = await store.PreviewUrlAsync(detail.BlobPath, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A missing preview must never stop a reviewer from deciding.
            LogPreviewFailed(logger, ex, detail.BlobPath);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostApproveAsync(int id, string? reason, bool bankVerified, CancellationToken cancellationToken) =>
        Done(id, await actions.ApproveAsync(id, Reviewer, reason, bankVerified, cancellationToken));

    public async Task<IActionResult> OnPostCorrectAsync(int id, string field, string? value, CancellationToken cancellationToken) =>
        Done(id, await actions.CorrectAsync(id, field, value, Reviewer, cancellationToken));

    public async Task<IActionResult> OnPostConfirmAliasAsync(int id, int lineMatchId, CancellationToken cancellationToken) =>
        Done(id, await actions.ConfirmAliasAsync(id, lineMatchId, Reviewer, cancellationToken));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not create a preview link for {BlobPath}.")]
    private static partial void LogPreviewFailed(ILogger logger, Exception exception, string blobPath);

    private RedirectToPageResult Done(int id, ReviewResult result)
    {
        TempData["Flash"] = result.Message;
        TempData["FlashKind"] = result.Succeeded ? "ok" : "error";
        return RedirectToPage(new { id });
    }
}

/// <summary>What the small "correct this value" form needs.</summary>
public sealed record EditFieldModel(int DocumentId, string Field, string? Current, string Label);
