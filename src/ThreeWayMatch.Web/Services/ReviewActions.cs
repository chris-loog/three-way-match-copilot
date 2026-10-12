using Microsoft.EntityFrameworkCore;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Normalisation;
using ThreeWayMatch.Infrastructure.Persistence;
using ThreeWayMatch.Infrastructure.Pipeline;

namespace ThreeWayMatch.Web.Services;

public sealed record ReviewResult(bool Succeeded, string Message);

/// <summary>What a reviewer can do. Each action checks its rules here, not in the page, so the rules are tested once.</summary>
public sealed class ReviewActions(ThreeWayMatchDbContext db, DocumentMatcher matcher, TimeProvider clock)
{
    public async Task<ReviewResult> ApproveAsync(int documentId, string reviewer, string? reason, bool bankVerified, CancellationToken cancellationToken)
    {
        var match = await db.MatchResults.Include(m => m.Discrepancies).SingleOrDefaultAsync(m => m.DocumentId == documentId, cancellationToken);
        if (match is null)
        {
            return new ReviewResult(false, "This document has not been matched yet.");
        }

        if (match.ReviewedAt is not null)
        {
            return new ReviewResult(false, "Someone has already approved this document.");
        }

        if (match.Decision is not (Decision.NeedsReview or Decision.OnHold))
        {
            return new ReviewResult(false, "Only documents that need review or are on hold can be approved by hand.");
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3)
        {
            return new ReviewResult(false, "Write why you are approving it. The reason is kept in the history.");
        }

        var changedBank = match.Discrepancies.Any(d => d.Code == DiscrepancyCode.BankAccountChanged);
        if (changedBank && !bankVerified)
        {
            return new ReviewResult(false, "The bank account changed. Call the supplier on a number you already had and tick the box before approving.");
        }

        match.ReviewedBy = reviewer;
        match.ReviewedAt = clock.GetUtcNow();
        match.OverrideReason = reason.Trim();
        match.BankChangeVerified = changedBank && bankVerified;
        await db.SaveChangesAsync(cancellationToken);
        return new ReviewResult(true, "Approved.");
    }

    /// <summary>Saves the correction, then matches the document again with it (no new read of the file).</summary>
    public async Task<ReviewResult> CorrectAsync(int documentId, string fieldName, string? value, string reviewer, CancellationToken cancellationToken)
    {
        if (!CorrectionApplier.TryValidate(fieldName, value, out var error))
        {
            return new ReviewResult(false, error);
        }

        var document = await db.Documents.Include(d => d.Extracted).ThenInclude(e => e!.Lines).Include(d => d.MatchResult).SingleOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        if (document?.Extracted is not { } extracted)
        {
            return new ReviewResult(false, "There is nothing extracted from this document to correct.");
        }

        if (document.MatchResult?.ReviewedAt is not null)
        {
            return new ReviewResult(false, "This document is already approved, so it can no longer be changed.");
        }

        var (before, confidence, line) = Current(extracted, fieldName);
        if (before is null && confidence is null && line is null && fieldName.Contains('.', StringComparison.Ordinal))
        {
            return new ReviewResult(false, $"{fieldName} does not exist on this document.");
        }

        db.FieldCorrections.Add(new FieldCorrection
        {
            ExtractedDocumentId = extracted.Id,
            ExtractedLineId = line?.Id,
            FieldName = fieldName,
            ExtractedValue = before,
            ExtractedConfidence = confidence,
            CorrectedValue = value!.Trim(),
            Reviewer = reviewer,
            CorrectedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);

        var status = await matcher.MatchAsync(documentId, cancellationToken);
        return new ReviewResult(true, $"Saved. The document was matched again: {Presentation.Label(status, reviewed: false).ToLowerInvariant()}.");
    }

    /// <summary>Confirms a description-only match: remembers the wording as an alias, so the same line matches by itself next time.</summary>
    public async Task<ReviewResult> ConfirmAliasAsync(int documentId, int lineMatchId, string reviewer, CancellationToken cancellationToken)
    {
        var lineMatch = await db.LineMatches
            .Include(l => l.ExtractedLine).Include(l => l.PurchaseOrderLine).Include(l => l.MatchResult)
            .SingleOrDefaultAsync(l => l.Id == lineMatchId && l.MatchResult!.DocumentId == documentId, cancellationToken);
        if (lineMatch is null)
        {
            return new ReviewResult(false, "That line was not found on this document.");
        }

        if (lineMatch.MatchResult!.ReviewedAt is not null)
        {
            return new ReviewResult(false, "This document is already approved, so it can no longer be changed.");
        }

        if (lineMatch.Method != MatchMethod.FuzzyDescription || lineMatch.PurchaseOrderLine is null)
        {
            return new ReviewResult(false, "Only a line matched by its description can be remembered.");
        }

        var description = Normaliser.NormaliseDescription(lineMatch.ExtractedLine?.Description);
        if (string.IsNullOrEmpty(description))
        {
            return new ReviewResult(false, "This line has no description to remember.");
        }

        var supplierItemId = lineMatch.PurchaseOrderLine.SupplierItemId;
        if (!await db.ItemAliases.AnyAsync(a => a.SupplierItemId == supplierItemId && a.NormalisedDescription == description, cancellationToken))
        {
            db.ItemAliases.Add(new ItemAlias { SupplierItemId = supplierItemId, NormalisedDescription = description, CreatedBy = reviewer, CreatedAt = clock.GetUtcNow() });
            await db.SaveChangesAsync(cancellationToken);
        }

        var status = await matcher.MatchAsync(documentId, cancellationToken);
        return new ReviewResult(true, $"Remembered. The document was matched again: {Presentation.Label(status, reviewed: false).ToLowerInvariant()}.");
    }

    private static (string? Value, double? Confidence, ExtractedLine? Line) Current(ExtractedDocument e, string fieldName)
    {
        var confidences = e.FieldConfidenceJson is { } json
            ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(json) ?? []
            : [];
        double? Confidence() => confidences.TryGetValue(fieldName, out var c) ? c : null;

        if (fieldName.StartsWith("Line", StringComparison.Ordinal) && fieldName.Contains('.', StringComparison.Ordinal))
        {
            var dot = fieldName.IndexOf('.', StringComparison.Ordinal);
            if (!int.TryParse(fieldName[4..dot], out var number) || e.Lines.FirstOrDefault(l => l.LineNumber == number) is not { } line)
            {
                return (null, null, null);
            }

            var text = fieldName[(dot + 1)..] switch
            {
                "ItemCode" => line.SupplierItemCode,
                "Unit" => line.Unit,
                "Quantity" => line.Quantity?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "UnitPrice" => line.UnitPrice?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => null,
            };
            return (text, Confidence(), line);
        }

        var value = fieldName switch
        {
            "InvoiceNumber" => e.DocumentNumber,
            "PurchaseOrderReference" => e.PurchaseOrderReference,
            "InvoiceDate" => e.DocumentDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            "SupplierVatNumber" => e.SupplierVatNumber,
            "NetTotal" => e.NetTotal?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "VatTotal" => e.VatTotal?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "GrossTotal" => e.GrossTotal?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Iban" => e.Iban,
            _ => null,
        };
        return (value, Confidence(), null);
    }
}
