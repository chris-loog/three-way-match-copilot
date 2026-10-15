using Microsoft.EntityFrameworkCore;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Tests.Extraction;

namespace ThreeWayMatch.Domain.Tests.Pipeline;

public class DeliveryNotePipelineTests
{
    private static readonly byte[] Note = [7, 7, 7];
    private static readonly byte[] Invoice = [1, 2, 3];

    /// <summary>A note from the supplier of <see cref="PipelineWorld"/>: plywood and MDF, in the layout of Baltic Board Supply.</summary>
    private static string NoteJson(string plywood = "283", IReadOnlyDictionary<string, double>? confidence = null) => LayoutResultBuilder.Build(
        "DELIVERY NOTE Baltic Board Supply OÜ\nDelivery note no.: DN-77\nDelivery date: 10 Apr 2026\nVAT no. EE109532815\nYour order: PO-2026-0003\nDeliver to\nNäidis Puit OÜ\nKMKR EE100000001",
        [["Pos", "Item code", "Description", "Unit", "Qty"], ["1", "BBS00157", "Spruce plywood 15 mm, 1220x2440", "pcs", plywood], ["2", "BBS00192", "MDF 16 mm raw, 2800x2070", "pcs", "185"]],
        "Goods received by:\nBaltic Board Supply OÜ · VAT no. EE109532815 · 1 / 1",
        confidence);

    private static async Task<DocumentStatus> RunNote(PipelineWorld world, string json)
    {
        using var db = world.NewContext();
        return await PipelineWorld.NewProcessor(db, new FakeExtractor("{}", deliveryNoteJson: json))
            .ProcessAsync("incoming/delivery-notes/dn.pdf", DocumentType.DeliveryNote, Note, CancellationToken.None);
    }

    private static async Task BookDeliveryNoteNumber(PipelineWorld world)
    {
        using var db = world.NewContext();
        (await db.GoodsReceipts.SingleAsync()).DeliveryNoteNumber = "DN-77";
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_delivery_note_that_agrees_with_the_booked_receipt_is_saved_and_approved()
    {
        using var world = new PipelineWorld();
        await BookDeliveryNoteNumber(world);

        var status = await RunNote(world, NoteJson());

        Assert.Equal(DocumentStatus.AutoApproved, status);
        using var db = world.NewContext();
        var document = await db.Documents.Include(d => d.Extracted!.Lines).Include(d => d.MatchResult!.Discrepancies).SingleAsync();
        Assert.Equal("DN-77", document.Extracted!.DocumentNumber);
        Assert.Equal("PO-2026-0003", document.Extracted.PurchaseOrderReference);
        Assert.Equal("prebuilt-layout", document.Extracted.ModelId);
        Assert.Equal(2, document.Extracted.Lines.Count);
        Assert.Empty(document.MatchResult!.Discrepancies);
    }

    [Fact]
    public async Task A_delivery_note_with_another_quantity_than_the_booked_receipt_goes_to_review()
    {
        using var world = new PipelineWorld();
        await BookDeliveryNoteNumber(world);

        var status = await RunNote(world, NoteJson(plywood: "280"));

        Assert.Equal(DocumentStatus.NeedsReview, status);
        using var db = world.NewContext();
        var result = await db.MatchResults.Include(m => m.Discrepancies).SingleAsync();
        Assert.Contains(result.Discrepancies, d => d.Code == DiscrepancyCode.DeliveryNoteMismatch);
    }

    [Fact]
    public async Task An_unsure_delivery_note_goes_to_review_with_low_confidence()
    {
        using var world = new PipelineWorld();
        await BookDeliveryNoteNumber(world);

        var status = await RunNote(world, NoteJson(confidence: new Dictionary<string, double> { ["DN-77"] = 0.4 }));

        Assert.Equal(DocumentStatus.NeedsReview, status);
        using var db = world.NewContext();
        var result = await db.MatchResults.Include(m => m.Discrepancies).SingleAsync();
        Assert.Contains(result.Discrepancies, d => d.Code == DiscrepancyCode.LowConfidence);
    }

    [Fact]
    public async Task A_delivery_note_does_not_use_up_the_quantity_an_invoice_may_bill()
    {
        using var world = new PipelineWorld();
        await BookDeliveryNoteNumber(world);
        await RunNote(world, NoteJson());

        using var db = world.NewContext();
        var invoiceStatus = await PipelineWorld.NewProcessor(db, new FakeExtractor(PipelineWorld.CleanResult()))
            .ProcessAsync("incoming/invoices/a.pdf", DocumentType.Invoice, Invoice, CancellationToken.None);

        Assert.Equal(DocumentStatus.AutoApproved, invoiceStatus);
    }

    [Fact]
    public async Task A_delivery_note_the_model_cannot_read_ends_as_failed_with_the_error()
    {
        using var world = new PipelineWorld();

        var status = await RunNote(world, "not json");

        Assert.Equal(DocumentStatus.Failed, status);
        using var db = world.NewContext();
        Assert.False(string.IsNullOrEmpty((await db.Documents.SingleAsync()).ErrorMessage));
    }
}
