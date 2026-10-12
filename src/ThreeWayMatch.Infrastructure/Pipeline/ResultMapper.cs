using System.Text.Json;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Domain.Rules;

namespace ThreeWayMatch.Infrastructure.Pipeline;

/// <summary>Turns the domain's plain results into the rows stored in the <c>ap</c> schema. No database calls here.</summary>
internal static class ResultMapper
{
    public static ExtractedDocument ToEntity(ExtractedInvoice invoice, DateTimeOffset now)
    {
        var entity = new ExtractedDocument
        {
            ModelId = invoice.ModelId,
            RawJson = invoice.RawJson,
            SupplierName = invoice.SupplierName.Value,
            SupplierVatNumber = invoice.SupplierVatNumber.Value,
            DocumentNumber = invoice.InvoiceNumber.Value,
            PurchaseOrderReference = invoice.PurchaseOrderReference.Value,
            DocumentDate = invoice.InvoiceDate.Value,
            NetTotal = invoice.NetTotal.Value,
            VatTotal = invoice.VatTotal.Value,
            GrossTotal = invoice.GrossTotal.Value,
            Currency = invoice.Currency.Value,
            Iban = invoice.Iban.Value,
            MinKeyFieldConfidence = invoice.MinKeyFieldConfidence(),
            FieldConfidenceJson = JsonSerializer.Serialize(invoice.KeyFieldConfidences()),
            ExtractedAt = now,
        };

        foreach (var line in invoice.Lines)
        {
            entity.Lines.Add(new ExtractedLine
            {
                LineNumber = line.LineNumber,
                SupplierItemCode = line.ItemCode.Value,
                Description = line.Description.Value,
                Quantity = line.Quantity.Value,
                Unit = line.Unit.Value,
                UnitPrice = line.UnitPrice.Value,
                VatRate = line.VatRate.Value,
                LineTotal = line.LineTotal.Value,
                Confidence = new[] { line.Quantity.Confidence, line.UnitPrice.Confidence }.Min(),
            });
        }

        return entity;
    }

    /// <summary>
    /// Stores what the rules acted on: the values after any reviewer corrections, with the confidence after the cross-check,
    /// so a reader sees the same document the decision was based on.
    /// </summary>
    public static void ApplyChecked(ExtractedDocument entity, ExtractedInvoice checkedInvoice)
    {
        entity.SupplierVatNumber = checkedInvoice.SupplierVatNumber.Value;
        entity.DocumentNumber = checkedInvoice.InvoiceNumber.Value;
        entity.PurchaseOrderReference = checkedInvoice.PurchaseOrderReference.Value;
        entity.DocumentDate = checkedInvoice.InvoiceDate.Value;
        entity.NetTotal = checkedInvoice.NetTotal.Value;
        entity.VatTotal = checkedInvoice.VatTotal.Value;
        entity.GrossTotal = checkedInvoice.GrossTotal.Value;
        entity.Iban = checkedInvoice.Iban.Value;
        entity.MinKeyFieldConfidence = checkedInvoice.MinKeyFieldConfidence();
        entity.FieldConfidenceJson = JsonSerializer.Serialize(checkedInvoice.KeyFieldConfidences());
        foreach (var line in checkedInvoice.Lines)
        {
            var stored = entity.Lines.FirstOrDefault(l => l.LineNumber == line.LineNumber);
            if (stored is not null)
            {
                stored.SupplierItemCode = line.ItemCode.Value;
                stored.Unit = line.Unit.Value;
                stored.Quantity = line.Quantity.Value;
                stored.UnitPrice = line.UnitPrice.Value;
                stored.Confidence = new[] { line.Quantity.Confidence, line.UnitPrice.Confidence }.Min();
            }
        }
    }

    /// <summary>
    /// The match result with its line matches and findings. Goods-receipt lines are looked up by id in
    /// <paramref name="receiptLines"/> (tracked entities), because the snapshot's copies are not tracked.
    /// </summary>
    public static MatchResult ToEntity(
        Assessment assessment, ExtractedDocument extracted, IReadOnlyDictionary<int, Domain.Erp.GoodsReceiptLine> receiptLines, DateTimeOffset now)
    {
        var result = new MatchResult
        {
            SupplierId = assessment.Match.Supplier.Supplier?.Id,
            PurchaseOrderId = assessment.Match.PurchaseOrder.PurchaseOrder?.Id,
            Decision = assessment.Decision,
            AmountAtStake = assessment.AmountAtStake,
            MatchedAt = now,
        };

        var lineMatches = new Dictionary<int, LineMatch>();
        foreach (var outcome in assessment.Match.Lines)
        {
            var lineMatch = new LineMatch
            {
                ExtractedLine = extracted.Lines.First(l => l.LineNumber == outcome.Line.LineNumber),
                PurchaseOrderLineId = outcome.PurchaseOrderLine?.Id,
                Method = outcome.Method,
                Confidence = outcome.Confidence,
                ConvertedQuantity = outcome.ConvertedQuantity,
            };
            lineMatch.GoodsReceiptLines.AddRange(outcome.ReceiptLines.Select(r => receiptLines[r.Id]));
            lineMatches[outcome.Line.LineNumber] = lineMatch;
            result.LineMatches.Add(lineMatch);
        }

        foreach (var finding in assessment.Findings)
        {
            result.Discrepancies.Add(new Discrepancy
            {
                Code = finding.Code,
                Severity = finding.Severity,
                LineMatch = finding.LineNumber is { } n && lineMatches.TryGetValue(n, out var match) ? match : null,
                ExpectedValue = finding.Expected,
                ActualValue = finding.Actual,
                AmountAtStake = finding.AmountAtStake,
            });
        }

        return result;
    }

    public static DocumentStatus ToStatus(Decision decision) => decision switch
    {
        Decision.AutoApproved => DocumentStatus.AutoApproved,
        Decision.OnHold => DocumentStatus.OnHold,
        Decision.NeedsReview => DocumentStatus.NeedsReview,
        Decision.Rejected => DocumentStatus.Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(decision)),
    };
}
