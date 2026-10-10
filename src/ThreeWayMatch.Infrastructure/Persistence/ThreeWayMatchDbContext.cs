using Microsoft.EntityFrameworkCore;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Erp;

namespace ThreeWayMatch.Infrastructure.Persistence;

public class ThreeWayMatchDbContext(DbContextOptions<ThreeWayMatchDbContext> options) : DbContext(options)
{
    public const string ErpSchema = "erp";
    public const string ApSchema = "ap";

    // erp: the mock ERP
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<SupplierItem> SupplierItems => Set<SupplierItem>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptLine> GoodsReceiptLines => Set<GoodsReceiptLine>();

    // ap: the application
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<ExtractedDocument> ExtractedDocuments => Set<ExtractedDocument>();
    public DbSet<ExtractedLine> ExtractedLines => Set<ExtractedLine>();
    public DbSet<MatchResult> MatchResults => Set<MatchResult>();
    public DbSet<LineMatch> LineMatches => Set<LineMatch>();
    public DbSet<Discrepancy> Discrepancies => Set<Discrepancy>();
    public DbSet<FieldCorrection> FieldCorrections => Set<FieldCorrection>();
    public DbSet<ItemAlias> ItemAliases => Set<ItemAlias>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored as readable text, not numbers.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(30);

        // Money by default. Quantities, VAT rates and conversion factors override this per column.
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // No cascading deletes unless a parent really owns its children (set explicitly below).
        // This also avoids SQL Server's "multiple cascade paths" error.
        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        }

        ErpModel.Configure(modelBuilder);
        ApModel.Configure(modelBuilder);
    }
}
