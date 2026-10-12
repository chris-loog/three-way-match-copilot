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
    /// Registers what the processing pipeline needs besides the database and the extractor:
    /// the rules, the snapshot loader and the processor. The processor is scoped because it shares one database context per message.
    /// </summary>
    public static IServiceCollection AddProcessingPipeline(this IServiceCollection services, DiscrepancyOptions? options = null)
    {
        services.AddSingleton(options ?? new DiscrepancyOptions());
        services.AddSingleton(DocumentAssessor.CreateDefault());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<SnapshotLoader>();
        services.AddScoped<DocumentProcessor>();
        return services;
    }
}
