using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ThreeWayMatch.Domain.Rules;
using ThreeWayMatch.Infrastructure.Persistence;
using ThreeWayMatch.Infrastructure.Pipeline;

namespace ThreeWayMatch.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the database. Retry on failure is on because the free serverless database pauses when idle,
    /// and the first connection after a pause fails or times out while it wakes up.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ThreeWayMatchDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null)));

        return services;
    }

    /// <summary>
    /// Registers the matching stage: the rules, the snapshot loader and the matcher. The review UI needs only this,
    /// because it re-matches after a correction but never reads a document itself.
    /// </summary>
    public static IServiceCollection AddMatching(this IServiceCollection services, DiscrepancyOptions? options = null)
    {
        services.AddSingleton(options ?? new DiscrepancyOptions());
        services.AddSingleton(DocumentAssessor.CreateDefault());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<SnapshotLoader>();
        services.AddScoped<DocumentMatcher>();
        return services;
    }

    /// <summary>Registers the whole processing pipeline: matching plus the processor that reads and records an uploaded file.</summary>
    public static IServiceCollection AddProcessingPipeline(this IServiceCollection services, DiscrepancyOptions? options = null)
    {
        services.AddMatching(options);
        services.AddScoped<DocumentProcessor>();
        return services;
    }
}
