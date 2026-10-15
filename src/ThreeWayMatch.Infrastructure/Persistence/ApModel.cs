using Microsoft.EntityFrameworkCore;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Erp;

namespace ThreeWayMatch.Infrastructure.Persistence;

internal static class ApModel
{
    private const string Schema = ThreeWayMatchDbContext.ApSchema;

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>(e =>
        {
            e.ToTable("Document", Schema, t => t.IsTemporal());
            e.Property(x => x.BlobPath).HasMaxLength(500);
            e.Property(x => x.ContentHash).HasMaxLength(64).IsFixedLength();
            e.Property(x => x.ErrorMessage).HasMaxLength(2000);
            // One row per uploaded blob: a retried queue message finds its row and resumes. The hash is not unique,
            // because a resent file must still become a row of its own so DUPLICATE_INVOICE can reject it.
            e.HasIndex(x => x.BlobPath).IsUnique();
            e.HasIndex(x => x.ContentHash);
            e.HasIndex(x => x.Status);
            e.HasOne(x => x.Extracted).WithOne(x => x.Document).HasForeignKey<ExtractedDocument>(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.MatchResult).WithOne(x => x.Document).HasForeignKey<MatchResult>(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExtractedDocument>(e =>
        {
            e.ToTable("ExtractedDocument", Schema);
            e.Property(x => x.ModelId).HasMaxLength(100);
            e.Property(x => x.SupplierName).HasMaxLength(200);
            e.Property(x => x.SupplierVatNumber).HasMaxLength(30);
            e.Property(x => x.DocumentNumber).HasMaxLength(50);
            e.Property(x => x.PurchaseOrderReference).HasMaxLength(50);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.Iban).HasMaxLength(50);
            // RawJson, FieldConfidenceJson: nvarchar(max) on purpose.
            e.HasMany(x => x.Lines).WithOne(l => l.ExtractedDocument).HasForeignKey(l => l.ExtractedDocumentId)
                .OnDelete(DeleteBehavior.Cascade);
            // Duplicate checks look up by supplier + document number.
            e.HasIndex(x => new { x.SupplierVatNumber, x.DocumentNumber });
        });

        modelBuilder.Entity<ExtractedLine>(e =>
        {
            e.ToTable("ExtractedLine", Schema);
            e.Property(x => x.SupplierItemCode).HasMaxLength(50);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.Unit).HasMaxLength(20);
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.Property(x => x.VatRate).HasPrecision(5, 4);
        });

        modelBuilder.Entity<MatchResult>(e =>
        {
            e.ToTable("MatchResult", Schema, t => t.IsTemporal());
            e.Property(x => x.OverrideReason).HasMaxLength(1000);
            e.Property(x => x.ReviewedBy).HasMaxLength(200);
            e.HasIndex(x => x.Decision);
            e.HasMany(x => x.LineMatches).WithOne(l => l.MatchResult).HasForeignKey(l => l.MatchResultId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Discrepancies).WithOne(d => d.MatchResult).HasForeignKey(d => d.MatchResultId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LineMatch>(e =>
        {
            e.ToTable("LineMatch", Schema);
            e.Property(x => x.ConvertedQuantity).HasPrecision(18, 4);
            e.HasMany(x => x.GoodsReceiptLines).WithMany()
                .UsingEntity<Dictionary<string, object>>(
                    "LineMatchGoodsReceiptLine",
                    right => right.HasOne<GoodsReceiptLine>().WithMany().HasForeignKey("GoodsReceiptLineId")
                        .OnDelete(DeleteBehavior.Restrict),
                    left => left.HasOne<LineMatch>().WithMany().HasForeignKey("LineMatchId")
                        .OnDelete(DeleteBehavior.Cascade),
                    join => join.ToTable("LineMatchGoodsReceiptLine", Schema));
        });

        modelBuilder.Entity<Discrepancy>(e =>
        {
            e.ToTable("Discrepancy", Schema);
            e.Property(x => x.ExpectedValue).HasMaxLength(200);
            e.Property(x => x.ActualValue).HasMaxLength(200);
        });

        modelBuilder.Entity<FieldCorrection>(e =>
        {
            e.ToTable("FieldCorrection", Schema);
            e.Property(x => x.FieldName).HasMaxLength(100);
            e.Property(x => x.ExtractedValue).HasMaxLength(500);
            e.Property(x => x.CorrectedValue).HasMaxLength(500);
            e.Property(x => x.Reviewer).HasMaxLength(200);
            e.HasOne(x => x.ExtractedDocument).WithMany().HasForeignKey(x => x.ExtractedDocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MatchExplanation>(e =>
        {
            e.ToTable("MatchExplanation", Schema);
            e.Property(x => x.ModelId).HasMaxLength(100);
            e.Property(x => x.Summary).HasMaxLength(2000);
            e.Property(x => x.LikelyCause).HasMaxLength(2000);
            e.Property(x => x.SuggestedAction).HasMaxLength(2000);
            e.Property(x => x.DraftMessage).HasMaxLength(4000);
            e.Property(x => x.DraftLanguage).HasMaxLength(30);
            e.Property(x => x.GuardNotes).HasMaxLength(2000);
            // One explanation per document; deleting the document deletes it.
            e.HasIndex(x => x.DocumentId).IsUnique();
            e.HasOne(x => x.Document).WithOne().HasForeignKey<MatchExplanation>(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ItemAlias>(e =>
        {
            e.ToTable("ItemAlias", Schema);
            e.Property(x => x.NormalisedDescription).HasMaxLength(300);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.HasIndex(x => new { x.SupplierItemId, x.NormalisedDescription }).IsUnique();
        });
    }
}
