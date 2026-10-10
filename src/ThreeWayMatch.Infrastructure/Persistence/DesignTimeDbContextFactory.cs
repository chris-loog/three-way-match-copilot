using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ThreeWayMatch.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the context without the app running. Generating migrations and scripts does not
/// connect to a database; applying them does, using the connection string in THREEWAYMATCH_SQL.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ThreeWayMatchDbContext>
{
    public ThreeWayMatchDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("THREEWAYMATCH_SQL")
            ?? "Server=localhost;Database=ThreeWayMatch;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<ThreeWayMatchDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new ThreeWayMatchDbContext(options);
    }
}
