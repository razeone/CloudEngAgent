using CloudEngAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Xunit;

namespace CloudEngAgent.Infrastructure.Tests;

[CollectionDefinition("MsSql")]
public sealed class MsSqlCollectionDefinition : ICollectionFixture<MsSqlContainerFixture>;

public sealed class MsSqlContainerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public string? SkipReason { get; private set; }

    public IDbContextFactory<RunsDbContext>? DbContextFactory { get; private set; }

    public async Task InitializeAsync()
    {
        if (!IsDockerAvailable())
        {
            SkipReason = "Docker is not available on this machine – skipping Testcontainers tests.";
            return;
        }

        _container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("Strong!Passw0rd#99")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .Build();

        try
        {
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            SkipReason = $"Failed to start MsSql container: {ex.Message}";
            _container = null;
            return;
        }

        var cs = _container.GetConnectionString();
        var options = new DbContextOptionsBuilder<RunsDbContext>()
            .UseSqlServer(cs)
            .Options;

        await using var ctx = new RunsDbContext(options);
        await ctx.Database.MigrateAsync();

        DbContextFactory = new TestDbContextFactory(cs);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    private static bool IsDockerAvailable()
    {
        // On Linux we check /var/run/docker.sock; on Windows we try the named pipe.
        // A quick heuristic: try running `docker info` and see if it exits cleanly.
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "info",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.Start();
            process.WaitForExit(5_000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Simple factory that creates a new <see cref="RunsDbContext"/> per call.</summary>
    private sealed class TestDbContextFactory(string connectionString) : IDbContextFactory<RunsDbContext>
    {
        public RunsDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<RunsDbContext>()
                .UseSqlServer(connectionString)
                .Options;
            return new RunsDbContext(options);
        }
    }
}
