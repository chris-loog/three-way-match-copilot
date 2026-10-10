using Microsoft.EntityFrameworkCore;
using ThreeWayMatch.Domain.Erp;
using ThreeWayMatch.Infrastructure.Persistence;

namespace DocGen.Seeding;

internal static class SeedCommand
{
    public const string ConnectionStringVariable = "THREEWAYMATCH_SQL";

    public static async Task<int> RunAsync(string[] args)
    {
        var dryRun = args.Contains("--dry-run");

        var data = new ErpDataBuilder(ErpDataBuilder.DefaultSeed).Build();
        PrintSummary(data);

        if (dryRun)
        {
            Console.WriteLine("Dry run: nothing was written.");
            return 0;
        }

        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine($"Set {ConnectionStringVariable} to the Azure SQL connection string (see PROGRESS.md, session log).");
            return 1;
        }

        var options = new DbContextOptionsBuilder<ThreeWayMatchDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null))
            .Options;

        await using var db = new ThreeWayMatchDbContext(options);
        Console.WriteLine("Writing to the erp schema. The first connection can take a minute if the database was paused...");
        await ErpSeeder.ReplaceAsync(db, data, CancellationToken.None);
        Console.WriteLine("Done.");
        return 0;
    }

    private static void PrintSummary(ErpData data)
    {
        var lines = data.PurchaseOrders.SelectMany(po => po.Lines).ToList();
        var orders = data.PurchaseOrders;

        int Orders(Func<PurchaseOrder, bool> predicate) => orders.Count(predicate);
        bool AllLines(PurchaseOrder po, PurchaseOrderLineStatus status) => po.Lines.All(l => l.Status == status);

        var fully = Orders(po => AllLines(po, PurchaseOrderLineStatus.Received));
        var none = Orders(po => AllLines(po, PurchaseOrderLineStatus.Open));
        var partly = orders.Count - fully - none;

        Console.WriteLine($"Suppliers:        {data.Suppliers.Count}");
        Console.WriteLine($"Items:            {data.Items.Count} ({data.SupplierItems.Count} supplier items)");
        Console.WriteLine($"Purchase orders:  {orders.Count} ({lines.Count} lines), {orders.Min(o => o.OrderDate):yyyy-MM-dd} to {orders.Max(o => o.OrderDate):yyyy-MM-dd}");
        Console.WriteLine($"  fully received: {fully} ({Percent(fully, orders.Count)})");
        Console.WriteLine($"  partly:         {partly} ({Percent(partly, orders.Count)})");
        Console.WriteLine($"  not received:   {none} ({Percent(none, orders.Count)})");
        Console.WriteLine($"Valid IBAN checksums: {data.Suppliers.Count(s => IbanGenerator.HasValidChecksum(s.Iban))} of {data.Suppliers.Count}");
        Console.WriteLine($"Goods receipts:   {data.GoodsReceipts.Count} ({data.GoodsReceipts.Sum(r => r.Lines.Count)} lines)");
    }

    private static string Percent(int part, int whole) =>
        (100.0 * part / whole).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " %";
}
