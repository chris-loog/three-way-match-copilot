using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ThreeWayMatch.Infrastructure;
using ThreeWayMatch.Infrastructure.DocumentIntelligence;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// Settings are read from environment variables: local.settings.json on your machine, application settings in Azure.
// There are no keys anywhere: every Azure service is reached with DefaultAzureCredential
// (your `az login` locally, the managed identity in Azure).
static string Required(string name) =>
    Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"Setting {name} is missing.");

builder.Services.AddInfrastructure(Required("THREEWAYMATCH_SQL"));
builder.Services.AddDocumentIntelligence(new DocumentIntelligenceOptions
{
    Endpoint = new Uri(Required("THREEWAYMATCH_DI_ENDPOINT")),
    CacheDirectory = Environment.GetEnvironmentVariable("THREEWAYMATCH_DI_CACHE"),
    // The delivery-note model: the custom model's id once it is trained, prebuilt-layout (text and tables plus rules) until then.
    DeliveryNoteModelId = Environment.GetEnvironmentVariable("THREEWAYMATCH_DI_DELIVERY_NOTE_MODEL") ?? "prebuilt-layout",
    // Free tier: 3 s between calls. On the paid tier set this to 0.
    MinimumCallInterval = TimeSpan.FromSeconds(double.Parse(
        Environment.GetEnvironmentVariable("THREEWAYMATCH_DI_MIN_INTERVAL_SECONDS") ?? "3", System.Globalization.CultureInfo.InvariantCulture)),
});
builder.Services.AddProcessingPipeline();
builder.Services.AddSingleton(_ => new BlobServiceClient(new Uri(Required("Storage__blobServiceUri")), new DefaultAzureCredential()));

builder.Build().Run();
