using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Erp;

namespace DocGen.Generation;

/// <summary>What is printed on an invoice line.</summary>
internal sealed class InvoiceLineModel
{
    public required int LineNumber { get; set; }
    public required string Code { get; set; }
    public required string Description { get; set; }
    public required decimal Quantity { get; set; }
    public required string Unit { get; set; }
    public required decimal UnitPrice { get; set; }
    public required decimal VatRate { get; set; }
    public required decimal LineTotal { get; set; }

    /// <summary>The PO line this line was built from; null for lines added by an injection.</summary>
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }
}

/// <summary>What is printed on an invoice, including any injected errors.</summary>
internal sealed class InvoiceModel
{
    public required Supplier Supplier { get; init; }
    public required LayoutDefinition Layout { get; init; }
    public required string InvoiceNumber { get; set; }
    public required DateOnly InvoiceDate { get; set; }
    public required DateOnly DueDate { get; set; }
    public string? PoReference { get; set; }
    public required string Iban { get; set; }
    public required List<InvoiceLineModel> Lines { get; init; }
    public required decimal NetTotal { get; set; }
    public required decimal VatTotal { get; set; }
    public required decimal GrossTotal { get; set; }
    public string Currency { get; } = "EUR";
}

internal sealed class DeliveryNoteLineModel
{
    public required int LineNumber { get; init; }
    public required string Code { get; init; }
    public required string Description { get; init; }
    public required decimal Quantity { get; set; }
    public required string Unit { get; init; }
    public GoodsReceiptLine? GoodsReceiptLine { get; init; }
}

internal sealed class DeliveryNoteModel
{
    public required Supplier Supplier { get; init; }
    public required LayoutDefinition Layout { get; init; }
    public required string DeliveryNoteNumber { get; init; }
    public required DateOnly DeliveryDate { get; init; }
    public required string PoReference { get; init; }
    public required List<DeliveryNoteLineModel> Lines { get; init; }
}

/// <summary>One finding that the generator put into a document on purpose.</summary>
internal sealed record InjectedDiscrepancy(
    DiscrepancyCode Code,
    Severity Severity,
    int? LineNumber,
    string? Expected,
    string? Actual,
    decimal AmountAtStake);

internal enum DocumentKind
{
    Invoice,
    DeliveryNote,
}

/// <summary>One document to render, with everything the ground truth needs to say about it.</summary>
internal sealed class PlannedDocument
{
    public required string Id { get; set; }
    public required DocumentKind Kind { get; init; }
    public required Supplier Supplier { get; init; }
    public required LayoutDefinition Layout { get; init; }
    public InvoiceModel? Invoice { get; init; }
    public DeliveryNoteModel? DeliveryNote { get; init; }
    public required string PurchaseOrderNumber { get; init; }
    public string? GoodsReceiptNumber { get; init; }
    public List<InjectedDiscrepancy> Injected { get; } = [];

    /// <summary>For an exact resend: the document whose file is copied byte for byte.</summary>
    public PlannedDocument? ExactCopyOf { get; init; }

    public Decision ExpectedDecision { get; set; } = Decision.AutoApproved;
    public string Split { get; set; } = "dev";
    public string Quality { get; set; } = "clean";
    public DateOnly SortDate { get; init; }
    public int SortOrder { get; init; }

    public string FileName => Kind == DocumentKind.Invoice ? $"invoices/{Id}.pdf" : $"delivery-notes/{Id}.pdf";
}
