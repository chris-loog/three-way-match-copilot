using Azure;
using Microsoft.EntityFrameworkCore;
using ThreeWayMatch.Domain.Ap;

namespace ThreeWayMatch.Domain.Tests.Pipeline;

public class DocumentProcessorTests
{
    private static readonly byte[] PdfA = [1, 2, 3];
    private static readonly byte[] PdfB = [4, 5, 6];

    private static Task<DocumentStatus> Run(PipelineWorld world, FakeExtractor extractor, string path, byte[] content, DocumentType type = DocumentType.Invoice)
    {
        using var db = world.NewContext();
        return PipelineWorld.NewProcessor(db, extractor).ProcessAsync(path, type, content, CancellationToken.None);
    }

    [Fact]
    public async Task A_perfectly_read_invoice_that_fits_the_order_is_auto_approved_and_saved()
    {
        using var world = new PipelineWorld();

        var status = await Run(world, new FakeExtractor(PipelineWorld.CleanResult()), "incoming/invoices/a.pdf", PdfA);

        Assert.Equal(DocumentStatus.AutoApproved, status);
        using var db = world.NewContext();
        var document = await db.Documents.Include(d => d.Extracted!.Lines).Include(d => d.MatchResult!.LineMatches).SingleAsync();
        Assert.Equal(DocumentStatus.AutoApproved, document.Status);
        Assert.Equal("BBS/2026/0354", document.Extracted!.DocumentNumber);
        Assert.Equal(3, document.Extracted.Lines.Count);
        Assert.Equal(Decision.AutoApproved, document.MatchResult!.Decision);
        Assert.Equal(3, document.MatchResult.LineMatches.Count);
        Assert.All(document.MatchResult.LineMatches, l => Assert.NotNull(l.PurchaseOrderLineId));
    }

    [Fact]
    public async Task An_unsure_extraction_goes_to_review_with_the_finding_saved()
    {
        using var world = new PipelineWorld();

        var status = await Run(world, new FakeExtractor(PipelineWorld.SmudgedResult()), "incoming/invoices/a.pdf", PdfA);

        Assert.Equal(DocumentStatus.NeedsReview, status);
        using var db = world.NewContext();
        var result = await db.MatchResults.Include(m => m.Discrepancies).SingleAsync();
        Assert.Contains(result.Discrepancies, d => d.Code == DiscrepancyCode.LowConfidence);
    }

    [Fact]
    public async Task A_redelivered_message_does_nothing_twice()
    {
        using var world = new PipelineWorld();
        var extractor = new FakeExtractor(PipelineWorld.CleanResult());

        await Run(world, extractor, "incoming/invoices/a.pdf", PdfA);
        var status = await Run(world, extractor, "incoming/invoices/a.pdf", PdfA);

        Assert.Equal(DocumentStatus.AutoApproved, status);
        Assert.Equal(1, extractor.Calls);
        using var db = world.NewContext();
        Assert.Equal(1, await db.Documents.CountAsync());
        Assert.Equal(1, await db.MatchResults.CountAsync());
    }

    [Fact]
    public async Task The_same_file_uploaded_again_under_a_new_name_is_rejected_as_a_duplicate()
    {
        using var world = new PipelineWorld();
        var extractor = new FakeExtractor(PipelineWorld.CleanResult());

        await Run(world, extractor, "incoming/invoices/a.pdf", PdfA);
        var status = await Run(world, extractor, "incoming/invoices/a-again.pdf", PdfA);

        Assert.Equal(DocumentStatus.Rejected, status);
        using var db = world.NewContext();
        var second = await db.Documents.Include(d => d.MatchResult!.Discrepancies).SingleAsync(d => d.BlobPath.EndsWith("a-again.pdf", StringComparison.Ordinal));
        Assert.Contains(second.MatchResult!.Discrepancies, d => d.Code == DiscrepancyCode.DuplicateInvoice);
    }

    [Fact]
    public async Task A_second_invoice_cannot_bill_what_the_first_approved_invoice_already_billed()
    {
        using var world = new PipelineWorld();

        await Run(world, new FakeExtractor(PipelineWorld.CleanResult()), "incoming/invoices/a.pdf", PdfA);
        var second = PipelineWorld.WithInvoiceNumber(PipelineWorld.CleanResult(), "BBS/2026/0999");
        var status = await Run(world, new FakeExtractor(second), "incoming/invoices/b.pdf", PdfB);

        Assert.Equal(DocumentStatus.NeedsReview, status);
        using var db = world.NewContext();
        var result = await db.MatchResults.Include(m => m.Discrepancies).SingleAsync(m => m.Document!.BlobPath.EndsWith("b.pdf", StringComparison.Ordinal));
        Assert.Contains(result.Discrepancies, d => d.Code == DiscrepancyCode.QtyOverInvoiced);
    }

    [Fact]
    public async Task An_invoice_that_arrives_before_the_goods_is_held_and_released_when_they_are_booked()
    {
        using var world = new PipelineWorld(received: false);

        var status = await Run(world, new FakeExtractor(PipelineWorld.CleanResult()), "incoming/invoices/a.pdf", PdfA);
        Assert.Equal(DocumentStatus.OnHold, status);

        world.BookFullReceipt();
        using var db = world.NewContext();
        var released = await PipelineWorld.NewProcessor(db, new FakeExtractor(PipelineWorld.CleanResult())).RematchOnHoldAsync(CancellationToken.None);

        Assert.Equal(1, released);
        using var check = world.NewContext();
        Assert.Equal(DocumentStatus.AutoApproved, (await check.Documents.SingleAsync()).Status);
        Assert.Equal(1, await check.MatchResults.CountAsync());
    }

    [Fact]
    public async Task A_file_the_service_cannot_read_ends_as_failed_with_the_error_saved()
    {
        using var world = new PipelineWorld();
        var extractor = new FakeExtractor("{}", new RequestFailedException(400, "The file is corrupt."));

        var status = await Run(world, extractor, "incoming/invoices/bad.pdf", PdfA);

        Assert.Equal(DocumentStatus.Failed, status);
        using var db = world.NewContext();
        var document = await db.Documents.SingleAsync();
        Assert.Contains("corrupt", document.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_busy_service_makes_the_message_retry_and_the_document_keeps_its_place()
    {
        using var world = new PipelineWorld();
        var busy = new FakeExtractor("{}", new RequestFailedException(429, "Too many requests."));

        await Assert.ThrowsAsync<RequestFailedException>(() => Run(world, busy, "incoming/invoices/a.pdf", PdfA));

        using (var db = world.NewContext())
        {
            Assert.Equal(DocumentStatus.Received, (await db.Documents.SingleAsync()).Status);
        }

        // The retry finds the row and carries on.
        var status = await Run(world, new FakeExtractor(PipelineWorld.CleanResult()), "incoming/invoices/a.pdf", PdfA);
        Assert.Equal(DocumentStatus.AutoApproved, status);
        using var after = world.NewContext();
        Assert.Equal(1, await after.Documents.CountAsync());
    }

    [Fact]
    public async Task A_delivery_note_is_recorded_as_failed_until_its_model_exists()
    {
        using var world = new PipelineWorld();

        var status = await Run(world, new FakeExtractor("{}"), "incoming/delivery-notes/dn.pdf", PdfA, DocumentType.DeliveryNote);

        Assert.Equal(DocumentStatus.Failed, status);
    }

    [Fact]
    public async Task Overwriting_a_processed_blob_with_other_content_changes_nothing()
    {
        using var world = new PipelineWorld();
        var extractor = new FakeExtractor(PipelineWorld.CleanResult());
        await Run(world, extractor, "incoming/invoices/a.pdf", PdfA);

        var status = await Run(world, extractor, "incoming/invoices/a.pdf", PdfB);

        Assert.Equal(DocumentStatus.AutoApproved, status);
        Assert.Equal(1, extractor.Calls);
    }
}
