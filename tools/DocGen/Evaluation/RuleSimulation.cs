using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Erp;
using ThreeWayMatch.Domain.Extraction;
using ThreeWayMatch.Domain.Matching;
using ThreeWayMatch.Domain.Rules;
using DocGen.Generation;
using DocGen.Seeding;

namespace DocGen.Evaluation;

internal sealed record SimulatedResult(PlannedDocument Document, Assessment Assessment);

/// <summary>
/// Runs the real assessor over every generated document, as if extraction had read the printed text perfectly and the
/// documents arrived in id order. No Azure and no database: the ERP data is rebuilt from the same seed.
/// It answers "does the matching and rule logic catch what the generator injected?", separately from "can Document
/// Intelligence read the page?".
/// </summary>
internal static class RuleSimulation
{
    private const double PerfectConfidence = 0.95;

    public static IReadOnlyList<SimulatedResult> Run(ErpData erp, IReadOnlyList<PlannedDocument> documents, DiscrepancyOptions? options = null)
    {
        LinkEntities(erp);

        var invoiced = new Dictionary<int, decimal>();
        var snapshot = new MatchingSnapshot
        {
            Suppliers = erp.Suppliers,
            SupplierItems = erp.SupplierItems,
            PurchaseOrders = erp.PurchaseOrders,
            InvoicedQuantityByPurchaseOrderLine = invoiced,
        };

        var assessor = DocumentAssessor.CreateDefault();
        var history = new List<ProcessedInvoice>();
        var results = new List<SimulatedResult>();

        foreach (var document in documents.OrderBy(d => d.Kind).ThenBy(d => d.Id, StringComparer.Ordinal))
        {
            if (document.Kind == DocumentKind.DeliveryNote)
            {
                var note = document.DeliveryNote!;
                var input = new MatchInput(note.Supplier.VatNumber, note.Supplier.Name, note.PoReference,
                    note.Lines.Select(l => new MatchInputLine(l.LineNumber, l.Code, l.Description, l.Quantity, l.Unit, null)).ToList());
                results.Add(new SimulatedResult(document, assessor.AssessDeliveryNote(input, note.DeliveryNoteNumber, [], snapshot, options)));
                continue;
            }

            var invoice = ToExtractedInvoice(document.Invoice!);
            var hash = $"hash-{(document.ExactCopyOf ?? document).Id}";
            var assessment = assessor.AssessInvoice(invoice, hash, snapshot, history, options);
            results.Add(new SimulatedResult(document, assessment));

            history.Add(new ProcessedInvoice(hash, document.Supplier.Id, document.Invoice!.InvoiceNumber, document.Invoice.GrossTotal, document.Invoice.InvoiceDate,
                assessment.Match.PurchaseOrder.PurchaseOrder?.Id));
            if (assessment.Decision == Decision.AutoApproved)
            {
                foreach (var line in assessment.Match.Lines.Where(l => l.PurchaseOrderLine is not null && l.ConvertedQuantity is not null))
                {
                    var factor = line.SupplierItem!.ConversionFactorToBase;
                    invoiced[line.PurchaseOrderLine!.Id] = invoiced.GetValueOrDefault(line.PurchaseOrderLine.Id) + line.ConvertedQuantity!.Value / factor;
                }
            }
        }

        return results;
    }

    /// <summary>The seeded entities have no ids and no back-references yet; the database loader would provide both.</summary>
    private static void LinkEntities(ErpData erp)
    {
        var next = 0;
        foreach (var supplier in erp.Suppliers) { supplier.Id = ++next; }
        next = 0;
        foreach (var item in erp.Items) { item.Id = ++next; }
        next = 0;
        foreach (var supplierItem in erp.SupplierItems)
        {
            supplierItem.Id = ++next;
            supplierItem.SupplierId = supplierItem.Supplier!.Id;
            supplierItem.ItemId = supplierItem.Item!.Id;
        }

        next = 0;
        var lineId = 0;
        foreach (var order in erp.PurchaseOrders)
        {
            order.Id = ++next;
            order.SupplierId = order.Supplier!.Id;
            foreach (var line in order.Lines)
            {
                line.Id = ++lineId;
                line.PurchaseOrder = order;
                line.PurchaseOrderId = order.Id;
                line.SupplierItemId = line.SupplierItem!.Id;
            }
        }

        foreach (var receipt in erp.GoodsReceipts)
        {
            foreach (var receiptLine in receipt.Lines)
            {
                receiptLine.GoodsReceipt = receipt;
                receiptLine.PurchaseOrderLine!.ReceiptLines.Add(receiptLine);
            }
        }
    }

    /// <summary>What a perfect reading of the printed page would give: the printed text, in the supplier's own formats.</summary>
    private static ExtractedInvoice ToExtractedInvoice(InvoiceModel model)
    {
        var layout = model.Layout;
        ExtractedField<string> Text(string? value) => value is null ? ExtractedField.Missing<string>() : new(value, value, PerfectConfidence);
        ExtractedField<decimal?> Money(decimal value, bool fourDecimals = false) => new(value, layout.FormatMoney(value, fourDecimals), PerfectConfidence);

        return new ExtractedInvoice
        {
            ModelId = "ground-truth",
            RawJson = "{}",
            SupplierName = Text(model.Supplier.Name),
            SupplierVatNumber = new ExtractedField<string>(model.Supplier.VatNumber, $"{layout.Labels.VatNumber} {model.Supplier.VatNumber}", PerfectConfidence),
            InvoiceNumber = Text(model.InvoiceNumber),
            PurchaseOrderReference = Text(model.PoReference),
            InvoiceDate = new ExtractedField<DateOnly?>(model.InvoiceDate, layout.FormatDate(model.InvoiceDate), PerfectConfidence),
            NetTotal = Money(model.NetTotal),
            VatTotal = Money(model.VatTotal),
            GrossTotal = Money(model.GrossTotal),
            Currency = Text(model.Currency),
            Iban = new ExtractedField<string>(model.Iban, LayoutDefinition.FormatIban(model.Iban), PerfectConfidence),
            Lines = model.Lines.Select(l => new ExtractedInvoiceLine(
                l.LineNumber, Text(l.Code), Text(l.Description),
                new ExtractedField<decimal?>(l.Quantity, layout.FormatQuantity(l.Quantity), PerfectConfidence),
                Text(l.Unit), Money(l.UnitPrice, fourDecimals: true), ExtractedField.Missing<decimal?>(), Money(l.LineTotal))).ToList(),
        };
    }
}
