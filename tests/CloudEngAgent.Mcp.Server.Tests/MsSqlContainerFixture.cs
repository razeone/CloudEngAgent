using CloudEngAgent.Mcp.Server.Options;
using CloudEngAgent.Mcp.Server.Sql;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace CloudEngAgent.Mcp.Server.Tests;

[CollectionDefinition("MsSql")]
public sealed class MsSqlCollectionDefinition : ICollectionFixture<MsSqlContainerFixture>;

public sealed class MsSqlContainerFixture : IAsyncLifetime
{
    public const string SeededDatabaseName = "mcp_seed";
    public const string SeededSchema = "dbo";
    public const string SeededTable = "Customers";

    private MsSqlContainer? _container;

    public string? SkipReason { get; private set; }

    public ISqlConnectionFactory? Factory { get; private set; }

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

        var rootCs = _container.GetConnectionString();
        await SeedAsync(rootCs);

        var dbCs = new SqlConnectionStringBuilder(rootCs)
        {
            InitialCatalog = SeededDatabaseName,
        }.ConnectionString;

        var options = Microsoft.Extensions.Options.Options.Create(new SqlServerOptions
        {
            ConnectionStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["seed"] = dbCs,
                ["root"] = rootCs,
            },
        });
        Factory = new SqlConnectionFactory(options);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private static async Task SeedAsync(string rootConnectionString)
    {
        await using (var conn = new SqlConnection(rootConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"IF DB_ID('{SeededDatabaseName}') IS NULL CREATE DATABASE [{SeededDatabaseName}];";
            await cmd.ExecuteNonQueryAsync();
        }

        var dbCs = new SqlConnectionStringBuilder(rootConnectionString)
        {
            InitialCatalog = SeededDatabaseName,
        }.ConnectionString;

        await using (var conn = new SqlConnection(dbCs))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
IF OBJECT_ID('{SeededSchema}.{SeededTable}', 'U') IS NULL
BEGIN
    CREATE TABLE [{SeededSchema}].[{SeededTable}] (
        Id INT NOT NULL PRIMARY KEY,
        Name NVARCHAR(100) NOT NULL,
        Email NVARCHAR(200) NULL
    );

    INSERT INTO [{SeededSchema}].[{SeededTable}] (Id, Name, Email) VALUES
        (1, 'Ada Lovelace', 'ada@example.com'),
        (2, 'Alan Turing', 'alan@example.com'),
        (3, 'Grace Hopper', 'grace@example.com'),
        (4, 'Edsger Dijkstra', NULL),
        (5, 'Donald Knuth', 'don@example.com');
END
";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static bool IsDockerAvailable()
    {
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
}
