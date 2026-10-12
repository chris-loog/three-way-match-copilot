using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Infrastructure.Persistence;
using ThreeWayMatch.Infrastructure.Pipeline;

namespace ThreeWayMatch.Web.Services;

/// <summary>Everything the screens read. Nothing here changes data.</summary>
public sealed class ReviewQueries(ThreeWayMatchDbContext db)
{
    public async Task<InboxCounts> CountsAsync(CancellationToken cancellationToken)
    {
        var documents = db.Documents.AsNoTracking();
        return new InboxCounts(
            NeedsReview: await Filtered(documents, InboxFilter.NeedsReview).CountAsync(cancellationToken),
            OnHold: await Filtered(documents, InboxFilter.OnHold).CountAsync(cancellationToken),
            AutoApproved: await Filtered(documents, InboxFilter.AutoApproved).CountAsync(cancellationToken),
            Rejected: await Filtered(documents, InboxFilter.Rejected).CountAsync(cancellationToken),
            Reviewed: await Filtered(documents, InboxFilter.Reviewed).CountAsync(cancellationToken),
            Failed: await documents.CountAsync(d => d.Status == DocumentStatus.Failed, cancellationToken));
    }

    private static IQueryable<Document> Filtered(IQueryable<Document> documents, InboxFilter filter) => filter switch
    {
        InboxFilter.NeedsReview => documents.Where(d => d.Status == DocumentStatus.NeedsReview && (d.MatchResult == null || d.MatchResult.ReviewedAt == null)),
        InboxFilter.OnHold => documents.Where(d => d.Status == DocumentStatus.OnHold && (d.MatchResult == null || d.MatchResult.ReviewedAt == null)),
        InboxFilter.AutoApproved => documents.Where(d => d.Status == DocumentStatus.AutoApproved),
        InboxFilter.Rejected => documents.Where(d => d.Status == DocumentStatus.Rejected),
        _ => documents.Where(d => d.MatchResult != null && d.MatchResult.ReviewedAt != null),
    };

    /// <summary>The documents behind one inbox tab, biggest money at risk first.</summary>
    public async Task<IReadOnlyList<InboxRow>> InboxAsync(InboxFilter filter, CancellationToken cancellationToken)
    {
        var rows = await Filtered(db.Documents.AsNoTracking(), filter)
            .OrderByDescending(d => d.MatchResult == null ? 0m : d.MatchResult.AmountAtStake)
            .ThenByDescending(d => d.ReceivedAt)
            .Take(200)
            .Select(d => new
            {
                d.Id,
                Supplier = d.MatchResult != null && d.MatchResult.Supplier != null ? d.MatchResult.Supplier.Name : d.Extracted != null ? d.Extracted.SupplierName : null,
                Number = d.Extracted == null ? null : d.Extracted.DocumentNumber,
                Gross = d.Extracted == null ? null : d.Extracted.GrossTotal,
                Currency = d.Extracted == null ? null : d.Extracted.Currency,
                Amount = d.MatchResult == null ? 0m : d.MatchResult.AmountAtStake,
                PurchaseOrder = d.MatchResult != null && d.MatchResult.PurchaseOrder != null ? d.MatchResult.PurchaseOrder.Number : null,
                Findings = d.MatchResult == null
                    ? new List<FindingView>()
                    : d.MatchResult.Discrepancies.Select(x => new FindingView(x.Code, x.Severity, null, null, null, x.AmountAtStake)).ToList(),
                d.ReceivedAt,
                d.Status,
                Reviewed = d.MatchResult != null && d.MatchResult.ReviewedAt != null,
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new InboxRow(
            r.Id, r.Supplier ?? "Unknown supplier", r.Number, r.Gross, r.Currency, r.Amount,
            r.Findings.OrderByDescending(f => f.Severity).ThenBy(f => f.Code).ToList(), r.ReceivedAt, r.Status, r.Reviewed, r.PurchaseOrder)).ToList();
    }

    public async Task<IReadOnlyList<FailedRow>> FailedAsync(CancellationToken cancellationToken) =>
        await db.Documents.AsNoTracking().Where(d => d.Status == DocumentStatus.Failed)
            .OrderByDescending(d => d.ReceivedAt)
            .Select(d => new FailedRow(d.Id, d.BlobPath, d.Type, d.ErrorMessage, d.ReceivedAt))
            .ToListAsync(cancellationToken);

    private static readonly (string Name, string Label, bool Editable)[] HeaderLayout =
    [
        ("InvoiceNumber", "Invoice number", true),
        ("PurchaseOrderReference", "Order reference", true),
        ("InvoiceDate", "Invoice date", true),
        ("SupplierVatNumber", "Supplier VAT number", true),
        ("NetTotal", "Net total", true),
        ("VatTotal", "VAT", true),
        ("GrossTotal", "Total to pay", true),
        ("Iban", "Bank account (IBAN)", true),
    ];

    public async Task<DocumentDetail?> DetailAsync(int id, CancellationToken cancellationToken)
    {
        var document = await db.Documents.AsNoTracking()
            .Include(d => d.Extracted).ThenInclude(e => e!.Lines)
            .Include(d => d.MatchResult).ThenInclude(m => m!.Supplier)
            .Include(d => d.MatchResult).ThenInclude(m => m!.PurchaseOrder)
            .Include(d => d.MatchResult).ThenInclude(m => m!.Discrepancies)
            .Include(d => d.MatchResult).ThenInclude(m => m!.LineMatches)
            .AsSplitQuery()
            .SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return null;
        }

        var extracted = document.Extracted;
        var match = document.MatchResult;
        var corrected = extracted is null
            ? new HashSet<string>()
            : (await db.FieldCorrections.AsNoTracking().Where(c => c.ExtractedDocumentId == extracted.Id).Select(c => c.FieldName).ToListAsync(cancellationToken)).ToHashSet();

        var confidences = extracted?.FieldConfidenceJson is { } json
            ? JsonSerializer.Deserialize<Dictionary<string, double>>(json) ?? []
            : [];

        var header = extracted is null ? [] : HeaderLayout.Select(f => HeaderField(extracted, f.Name, f.Label, f.Editable, confidences, corrected)).ToList();
        var findingsByLineMatch = match?.Discrepancies.Where(d => d.LineMatchId != null).ToLookup(d => d.LineMatchId!.Value) ?? Enumerable.Empty<Discrepancy>().ToLookup(d => 0);
        var lineNumberByLineMatch = extracted is null || match is null
            ? []
            : match.LineMatches.ToDictionary(m => m.Id, m => extracted.Lines.FirstOrDefault(l => l.Id == m.ExtractedLineId)?.LineNumber);

        var findings = (match?.Discrepancies ?? [])
            .Select(d => new FindingView(d.Code, d.Severity, d.LineMatchId is { } lm ? lineNumberByLineMatch.GetValueOrDefault(lm) : null, d.ExpectedValue, d.ActualValue, d.AmountAtStake))
            .OrderByDescending(f => f.Severity).ThenByDescending(f => f.AmountAtStake).ToList();

        var lines = extracted is null ? [] : await LinesAsync(document, extracted, match, findingsByLineMatch, confidences, cancellationToken);

        return new DocumentDetail(
            document.Id, document.BlobPath, document.Type, document.Status, extracted?.ModelId ?? "-", document.ReceivedAt,
            match?.Decision, match?.AmountAtStake ?? 0, match?.Supplier?.Name ?? extracted?.SupplierName, match?.PurchaseOrder?.Number, extracted?.Currency,
            match?.ReviewedAt != null, match?.ReviewedBy, match?.ReviewedAt, match?.OverrideReason, match?.BankChangeVerified ?? false, document.ErrorMessage,
            header, lines, findings);
    }

    private static FieldView HeaderField(ExtractedDocument e, string name, string label, bool editable, Dictionary<string, double> confidences, HashSet<string> corrected)
    {
        var (display, edit) = name switch
        {
            "InvoiceNumber" => (e.DocumentNumber, e.DocumentNumber),
            "PurchaseOrderReference" => (e.PurchaseOrderReference, e.PurchaseOrderReference),
            "InvoiceDate" => (e.DocumentDate?.ToString("d MMM yyyy", CultureInfo.InvariantCulture), e.DocumentDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            "SupplierVatNumber" => (e.SupplierVatNumber, e.SupplierVatNumber),
            "NetTotal" => (Presentation.Money(e.NetTotal, e.Currency), e.NetTotal?.ToString("0.00##", CultureInfo.InvariantCulture)),
            "VatTotal" => (Presentation.Money(e.VatTotal, e.Currency), e.VatTotal?.ToString("0.00##", CultureInfo.InvariantCulture)),
            "GrossTotal" => (Presentation.Money(e.GrossTotal, e.Currency), e.GrossTotal?.ToString("0.00##", CultureInfo.InvariantCulture)),
            _ => (e.Iban, e.Iban),
        };

        return new FieldView(name, label, display == "-" ? null : display, edit, confidences.TryGetValue(name, out var c) ? c : null, corrected.Contains(name), editable);
    }

    private async Task<IReadOnlyList<LineView>> LinesAsync(
        Document document, ExtractedDocument extracted, MatchResult? match, ILookup<int, Discrepancy> findingsByLineMatch,
        Dictionary<string, double> confidences, CancellationToken cancellationToken)
    {
        var poLineIds = match?.LineMatches.Where(m => m.PurchaseOrderLineId != null).Select(m => m.PurchaseOrderLineId!.Value).Distinct().ToList() ?? [];
        var poLines = await db.PurchaseOrderLines.AsNoTracking()
            .Include(l => l.SupplierItem).ThenInclude(s => s!.Item)
            .Include(l => l.ReceiptLines)
            .Where(l => poLineIds.Contains(l.Id))
            .AsSplitQuery()
            .ToDictionaryAsync(l => l.Id, cancellationToken);

        // What approved invoices (other than this one) already billed, in the item's base unit.
        var before = await db.LineMatches.AsNoTracking()
            .Where(l => l.PurchaseOrderLineId != null && poLineIds.Contains(l.PurchaseOrderLineId.Value) && l.ConvertedQuantity != null
                && l.MatchResult!.DocumentId != document.Id
                && (l.MatchResult.Decision == Decision.AutoApproved || l.MatchResult.ReviewedAt != null))
            .Select(l => new { Id = l.PurchaseOrderLineId!.Value, Quantity = l.ConvertedQuantity!.Value })
            .ToListAsync(cancellationToken);
        var billedBefore = before.GroupBy(b => b.Id).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        var matchByLine = match?.LineMatches.ToDictionary(m => m.ExtractedLineId) ?? [];

        return extracted.Lines.OrderBy(l => l.LineNumber).Select(line =>
        {
            matchByLine.TryGetValue(line.Id, out var lineMatch);
            var po = lineMatch?.PurchaseOrderLineId is { } poId && poLines.TryGetValue(poId, out var found) ? found : null;

            Tally? tally = null;
            if (po?.SupplierItem?.Item is { } item && lineMatch?.ConvertedQuantity is { } thisInvoice)
            {
                var factor = po.SupplierItem.ConversionFactorToBase;
                tally = new Tally(po.Quantity * factor, po.ReceiptLines.Sum(r => r.Quantity) * factor, billedBefore.GetValueOrDefault(po.Id), thisInvoice, item.BaseUnit);
            }

            var perLine = lineMatch is null ? [] : findingsByLineMatch[lineMatch.Id].Select(d => new FindingView(d.Code, d.Severity, line.LineNumber, d.ExpectedValue, d.ActualValue, d.AmountAtStake)).ToList();
            return new LineView(
                line.LineNumber, line.SupplierItemCode, line.Description, line.Quantity, line.Unit, line.UnitPrice, line.LineTotal, line.Confidence,
                lineMatch?.Id, lineMatch?.Method, lineMatch?.Confidence, po?.LineNumber, po?.Description, po?.Unit, po?.UnitPrice, tally, perLine);
        }).ToList();
    }

    public async Task<DashboardStats> DashboardAsync(CancellationToken cancellationToken)
    {
        var results = await db.MatchResults.AsNoTracking().Select(m => new { m.Decision, m.AmountAtStake, Reviewed = m.ReviewedAt != null }).ToListAsync(cancellationToken);
        var open = results.Where(r => r.Decision is Decision.NeedsReview or Decision.OnHold && !r.Reviewed).ToList();

        var findings = await db.Discrepancies.AsNoTracking().Where(d => d.Severity >= Severity.Warning).GroupBy(d => d.Code)
            .Select(g => new { Code = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);

        // Accuracy per field: how often a reviewer had to fix what the model read.
        var corrections = await db.FieldCorrections.AsNoTracking().Select(c => new { c.FieldName, Supplier = c.ExtractedDocument!.SupplierName }).ToListAsync(cancellationToken);
        var documents = await db.ExtractedDocuments.AsNoTracking().Select(e => new { e.SupplierName }).ToListAsync(cancellationToken);
        var lineCount = await db.ExtractedLines.CountAsync(cancellationToken);

        static string Plain(string field) => field.Contains('.', StringComparison.Ordinal) ? field[(field.IndexOf('.', StringComparison.Ordinal) + 1)..] : field;
        var fields = CorrectionApplier.HeaderFields.Select(f => (Field: f, Total: documents.Count))
            .Concat(CorrectionApplier.LineFields.Select(f => (Field: f, Total: lineCount)))
            .Select(f => (f.Field, Corrections: corrections.Count(c => Plain(c.FieldName) == f.Field), f.Total))
            .Where(f => f.Total > 0)
            .ToList();

        var suppliers = documents.GroupBy(d => d.SupplierName ?? "Unknown supplier")
            .Select(g => (Supplier: g.Key, Corrections: corrections.Count(c => (c.Supplier ?? "Unknown supplier") == g.Key), Documents: g.Count()))
            .OrderByDescending(s => s.Corrections).ThenBy(s => s.Supplier).ToList();

        return new DashboardStats(
            results.Count, results.Count(r => r.Decision == Decision.AutoApproved), open.Count, open.Sum(r => r.AmountAtStake),
            findings.OrderByDescending(f => f.Count).Select(f => (f.Code, f.Count)).ToList(),
            fields, suppliers,
            Enum.GetValues<Decision>().Select(d => (d.ToString(), results.Count(r => r.Decision == d))).ToList());
    }
}
