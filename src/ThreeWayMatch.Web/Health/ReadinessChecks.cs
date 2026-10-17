using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ThreeWayMatch.Infrastructure.Persistence;

namespace ThreeWayMatch.Web.Health;

/// <summary>
/// The database answers and has every migration applied. Code that is ahead of the schema fails here, so the smoke test after
/// a deploy tells you to run the migration bundle instead of letting users find out.
/// </summary>
public sealed class DatabaseCheck(ThreeWayMatchDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("The database does not answer.");
            }

            if (db.Database.IsRelational() && (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            {
                return HealthCheckResult.Unhealthy("The database has migrations that are not applied. Run the migration bundle.");
            }

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("The database check failed.");
        }
    }
}

/// <summary>The upload container can be read with the app's identity. Skipped when no storage account is configured (local development).</summary>
public sealed class StorageCheck(IServiceProvider services) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var blobs = services.GetService<BlobServiceClient>();
        if (blobs is null)
        {
            return HealthCheckResult.Healthy("Not configured.");
        }

        try
        {
            await blobs.GetBlobContainerClient("incoming").GetPropertiesAsync(cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("The upload container cannot be read.");
        }
    }
}

/// <summary>The queue that carries upload events to the Function exists and can be read. Skipped when not configured.</summary>
public sealed class QueueCheck(IServiceProvider services) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var queues = services.GetService<QueueServiceClient>();
        if (queues is null)
        {
            return HealthCheckResult.Healthy("Not configured.");
        }

        try
        {
            await queues.GetQueueClient("documents").GetPropertiesAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("The document queue cannot be read.");
        }
    }
}
