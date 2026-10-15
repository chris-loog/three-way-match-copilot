using System.Threading.RateLimiting;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using ThreeWayMatch.Infrastructure;
using ThreeWayMatch.Infrastructure.Explanation;
using ThreeWayMatch.Web;
using ThreeWayMatch.Web.Auth;
using ThreeWayMatch.Web.Services;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
var demo = configuration.GetValue<bool>("Demo:Enabled");

// Settings come from environment variables or appsettings. There are no keys: Azure is reached with DefaultAzureCredential.
builder.Services.AddInfrastructure(configuration["THREEWAYMATCH_SQL"] ?? throw new InvalidOperationException("Setting THREEWAYMATCH_SQL is missing."));
builder.Services.AddMatching();
builder.Services.AddScoped<ReviewQueries>();
builder.Services.AddScoped<ReviewActions>();
builder.Services.AddScoped<DemoData>();
builder.Services.AddSingleton<ExplainThrottle>();
// Azure OpenAI is optional: without an endpoint the explanation panel shows the standard text built from the findings.
builder.Services.AddExplanations(new ExplanationOptions
{
    Endpoint = Uri.TryCreate(configuration["OpenAI:Endpoint"], UriKind.Absolute, out var openAiEndpoint) ? openAiEndpoint : null,
    Deployment = configuration["OpenAI:Deployment"] ?? "gpt-4.1-mini",
});
builder.Services.AddSingleton(new DemoMode(demo));

if (!string.IsNullOrWhiteSpace(configuration["Storage:BlobServiceUri"]))
{
    builder.Services.AddSingleton(_ => new BlobServiceClient(new Uri(configuration["Storage:BlobServiceUri"]!), new DefaultAzureCredential()));
    builder.Services.AddSingleton<IDocumentStore, BlobDocumentStore>();
}
else
{
    builder.Services.AddSingleton<IDocumentStore, NoDocumentStore>();
}

// Demo mode: one fixed reviewer, no sign-in. Otherwise Entra ID, with the Reviewer app role.
if (demo)
{
    builder.Services.AddAuthentication(DemoAuthHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DemoAuthHandler>(DemoAuthHandler.SchemeName, null);
}
else
{
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(configuration.GetSection("AzureAd"));
}

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Reviewer, policy => policy.RequireRole(DemoAuthHandler.ReviewerRole));

// A public demo must not let one visitor fill the storage account: uploads are limited per address.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(Policies.Upload, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/", Policies.Reviewer);
    options.Conventions.AllowAnonymousToPage("/Error");
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();

/// <summary>Lets the integration tests start the app.</summary>
public partial class Program;
