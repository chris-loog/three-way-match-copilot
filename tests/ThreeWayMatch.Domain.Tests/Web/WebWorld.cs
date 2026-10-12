using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ThreeWayMatch.Domain.Ap;
using ThreeWayMatch.Domain.Tests.Pipeline;
using ThreeWayMatch.Infrastructure.Persistence;
using ThreeWayMatch.Web.Services;

namespace ThreeWayMatch.Domain.Tests.Web;

/// <summary>The review UI started in memory: an in-memory database with the small test ERP, and a fake document store.</summary>
internal sealed class WebWorld : WebApplicationFactory<ThreeWayMatch.Web.DemoMode>
{
    private readonly string _databaseName = Guid.NewGuid().ToString("N");

    public FakeStore Store { get; } = new();

    public WebWorld(bool received = true)
    {
        using var scope = Services.CreateScope();
        PipelineWorld.SeedErp(scope.ServiceProvider.GetRequiredService<ThreeWayMatchDbContext>(), received);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Demo:Enabled", "true");
        builder.UseSetting("THREEWAYMATCH_SQL", "not used in tests");
        builder.ConfigureServices(services =>
        {
            // Take out the SQL Server registration (the options and any configuration generic over our context), keep the context itself.
            foreach (var registration in services
                .Where(d => d.ServiceType != typeof(ThreeWayMatchDbContext)
                    && (d.ServiceType == typeof(DbContextOptions) || d.ServiceType.GenericTypeArguments.Contains(typeof(ThreeWayMatchDbContext))))
                .ToList())
            {
                services.Remove(registration);
            }

            services.AddDbContext<ThreeWayMatchDbContext>(options => options
                .UseInMemoryDatabase(_databaseName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            services.RemoveAll<IDocumentStore>();
            services.AddSingleton<IDocumentStore>(Store);
        });
    }

    /// <summary>Runs a document through the real pipeline against this world's database.</summary>
    public async Task<DocumentStatus> ProcessAsync(string path, string rawJson, byte[] content, DocumentType type = DocumentType.Invoice)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ThreeWayMatchDbContext>();
        return await PipelineWorld.NewProcessor(db, new FakeExtractor(rawJson)).ProcessAsync(path, type, content, CancellationToken.None);
    }

    public async Task<T> WithDbAsync<T>(Func<ThreeWayMatchDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ThreeWayMatchDbContext>());
    }

    public async Task<int> IdOfAsync(string pathEnd) =>
        await WithDbAsync(db => db.Documents.Where(d => d.BlobPath.EndsWith(pathEnd)).Select(d => d.Id).SingleAsync());

    public static ReviewActions NewActions(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<ReviewActions>();
}

internal sealed class FakeStore : IDocumentStore
{
    public List<(DocumentType Type, string Name, int Length)> Uploads { get; } = [];

    public async Task<string> UploadAsync(DocumentType type, string fileName, Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Uploads.Add((type, fileName, (int)buffer.Length));
        return "incoming/invoices/" + fileName;
    }

    public Task<Uri?> PreviewUrlAsync(string blobPath, CancellationToken cancellationToken) => Task.FromResult<Uri?>(null);
}
