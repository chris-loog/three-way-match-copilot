using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ThreeWayMatch.Web.Health;

public static class HealthEndpoint
{
    public static IServiceCollection AddReadinessChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseCheck>("database")
            .AddCheck<StorageCheck>("storage")
            .AddCheck<QueueCheck>("queue");
        return services;
    }

    /// <summary>
    /// <c>/health</c>: 200 when the database, storage and queue all answer, 503 otherwise. Open to anyone, so it says only
    /// which check failed and a fixed sentence, never an exception text. The deploy workflow calls it as its smoke test.
    /// </summary>
    public static IEndpointConventionBuilder MapReadiness(this IEndpointRouteBuilder app) =>
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.Body, new
                {
                    status = report.Status.ToString(),
                    checks = report.Entries.ToDictionary(e => e.Key, e => new { status = e.Value.Status.ToString(), detail = e.Value.Description }),
                });
            },
        }).AllowAnonymous();
}
