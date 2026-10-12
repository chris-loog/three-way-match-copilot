using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Web.Services;

namespace ThreeWayMatch.Web.Pages;

[EnableRateLimiting(Policies.Upload)]
[RequestSizeLimit(10_100_000)]
public sealed partial class UploadModel(IDocumentStore store, ILogger<UploadModel> logger) : PageModel
{
    private const int MaxBytes = 10_000_000;
    private static readonly string[] Allowed = [".pdf", ".jpg", ".jpeg", ".png"];

    [BindProperty]
    public DocumentType Type { get; set; } = DocumentType.Invoice;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(List<IFormFile>? files, CancellationToken cancellationToken)
    {
        if (files is null || files.Count == 0)
        {
            return Fail("Choose at least one file.");
        }

        var uploaded = 0;
        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!Allowed.Contains(extension))
            {
                return Fail($"{file.FileName} is not a PDF, JPG or PNG file.");
            }

            if (file.Length is 0 or > MaxBytes)
            {
                return Fail($"{file.FileName} must be between 1 byte and 10 MB.");
            }

            try
            {
                await using var stream = file.OpenReadStream();
                await store.UploadAsync(Type, file.FileName, stream, cancellationToken);
                uploaded++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogUploadFailed(logger, ex, file.FileName);
                return Fail($"{file.FileName} could not be saved. Try again in a moment.");
            }
        }

        TempData["Flash"] = $"{uploaded} {(uploaded == 1 ? "file" : "files")} uploaded. They appear in the lists in about a minute, once they have been read and matched.";
        return RedirectToPage("/Index");
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Upload of {FileName} failed.")]
    private static partial void LogUploadFailed(ILogger logger, Exception exception, string fileName);

    private PageResult Fail(string message)
    {
        ModelState.AddModelError(string.Empty, message);
        return Page();
    }
}
