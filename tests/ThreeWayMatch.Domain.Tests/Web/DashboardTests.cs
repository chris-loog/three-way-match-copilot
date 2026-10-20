using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ThreeWayMatch.Domain.Tests.Pipeline;
using ThreeWayMatch.Infrastructure.Persistence;
using ThreeWayMatch.Web.Services;

namespace ThreeWayMatch.Domain.Tests.Web;

public class DashboardTests
{
    [Fact]
    public async Task A_supplier_whose_name_was_misread_on_a_scan_still_counts_as_one_supplier()
    {
        using var world = new WebWorld();
        await world.ProcessAsync("incoming/invoices/a.pdf", PipelineWorld.CleanResult(), [1, 2, 3]);
        await world.ProcessAsync("incoming/invoices/b.pdf", PipelineWorld.WithInvoiceNumber(PipelineWorld.CleanResult(), "BBS/2026/0999"), [4, 5, 6]);
        using var scope = world.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ThreeWayMatchDbContext>();
        var second = await db.ExtractedDocuments.OrderByDescending(e => e.Id).FirstAsync();
        second.SupplierName = "Baltic Board Supply OD";
        await db.SaveChangesAsync();

        var stats = await scope.ServiceProvider.GetRequiredService<ReviewQueries>().DashboardAsync(CancellationToken.None);

        var supplier = Assert.Single(stats.SupplierCorrections);
        Assert.Equal(2, supplier.Documents);
        Assert.DoesNotContain("OD", supplier.Supplier, StringComparison.Ordinal);
    }
}
