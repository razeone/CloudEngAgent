using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CloudEngAgent.Infrastructure.Persistence;

/// <summary>
/// Used by EF Core design-time tooling (dotnet ef migrations).
/// Reads the connection string from the CLOUDENG_RUNS_CONNECTION environment variable,
/// or falls back to a LocalDB string suitable for local development.
/// </summary>
public sealed class DesignTimeRunsDbContextFactory : IDesignTimeDbContextFactory<RunsDbContext>
{
    public RunsDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("CLOUDENG_RUNS_CONNECTION")
            ?? "Server=(localdb)\\mssqllocaldb;Database=CloudEngAgent;Trusted_Connection=true;MultipleActiveResultSets=true";

        var optionsBuilder = new DbContextOptionsBuilder<RunsDbContext>();
        optionsBuilder.UseSqlServer(cs);

        return new RunsDbContext(optionsBuilder.Options);
    }
}
