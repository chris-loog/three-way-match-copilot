using Microsoft.EntityFrameworkCore;
using ThreeWayMatch.Domain.Erp;

namespace ThreeWayMatch.Infrastructure.Persistence;

internal static class ErpModel
{
    private const string Schema = ThreeWayMatchDbContext.ErpSchema;

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Supplier>(e =>
        {
            e.ToTable("Supplier", Schema);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Category).HasMaxLength(50);
            e.Property(x => x.VatNumber).HasMaxLength(20);
            e.Property(x => x.RegistryCode).HasMaxLength(20);
            e.Property(x => x.Iban).HasMaxLength(34);
            e.Property(x => x.DocumentCulture).HasMaxLength(10);
            e.Property(x => x.PriceTolerancePercent).HasPrecision(5, 2);
            e.Property(x => x.QuantityTolerancePercent).HasPrecision(5, 2);
            e.HasIndex(x => x.VatNumber).IsUnique();
        });

        modelBuilder.Entity<Item>(e =>
        {
            e.ToTable("Item", Schema);
            e.Property(x => x.Code).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.BaseUnit).HasMaxLength(20);
            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<SupplierItem>(e =>
        {
            e.ToTable("SupplierItem", Schema);
            e.Property(x => x.SupplierItemCode).HasMaxLength(50);
            e.Property(x => x.SupplierDescription).HasMaxLength(300);
            e.Property(x => x.SupplierUnit).HasMaxLength(20);
            e.Property(x => x.ConversionFactorToBase).HasPrecision(18, 6);
            e.HasIndex(x => new { x.SupplierId, x.SupplierItemCode }).IsUnique();
        });

        modelBuilder.Entity<PurchaseOrder>(e =>
        {
            e.ToTable("PurchaseOrder", Schema);
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.HasIndex(x => x.Number).IsUnique();
            e.HasMany(x => x.Lines).WithOne(l => l.PurchaseOrder).HasForeignKey(l => l.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PurchaseOrderLine>(e =>
        {
            // Temporal: SQL Server keeps every previous version of a row, so "what did this line look like on 3 March" is a query.
            e.ToTable("PurchaseOrderLine", Schema, t => t.IsTemporal());
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.Unit).HasMaxLength(20);
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.Property(x => x.VatRate).HasPrecision(5, 4);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.PurchaseOrderId, x.LineNumber }).IsUnique();
        });

        modelBuilder.Entity<GoodsReceipt>(e =>
        {
            e.ToTable("GoodsReceipt", Schema);
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.DeliveryNoteNumber).HasMaxLength(50);
            e.HasIndex(x => x.Number).IsUnique();
            e.HasMany(x => x.Lines).WithOne(l => l.GoodsReceipt).HasForeignKey(l => l.GoodsReceiptId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GoodsReceiptLine>(e =>
        {
            e.ToTable("GoodsReceiptLine", Schema);
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.HasIndex(x => x.PurchaseOrderLineId);
        });
    }
}
