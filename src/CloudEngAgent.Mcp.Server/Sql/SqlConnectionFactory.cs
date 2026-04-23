using CloudEngAgent.Mcp.Server.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using ModelContextProtocol;

namespace CloudEngAgent.Mcp.Server.Sql;

public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly IReadOnlyList<KeyValuePair<string, string>> _entries;

    public SqlConnectionFactory(IOptions<SqlServerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _entries = options.Value.ConnectionStrings
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
            .ToArray();
    }

    public IReadOnlyList<string> ConfiguredDatabases =>
        _entries.Select(e => e.Key).ToArray();

    public async Task<SqlConnection> OpenAsync(string? database, CancellationToken cancellationToken)
    {
        if (_entries.Count == 0)
        {
            throw new McpException(
                "No SQL Server connection strings are configured. Set 'Mcp:SqlServer:ConnectionStrings' in configuration.",
                McpErrorCode.InvalidParams);
        }

        string connectionString;
        if (string.IsNullOrWhiteSpace(database))
        {
            connectionString = _entries[0].Value;
        }
        else
        {
            var match = _entries.FirstOrDefault(
                e => string.Equals(e.Key, database, StringComparison.OrdinalIgnoreCase));
            if (match.Value is null)
            {
                var known = string.Join(", ", _entries.Select(e => e.Key));
                throw new McpException(
                    $"Unknown database '{database}'. Configured: [{known}].",
                    McpErrorCode.InvalidParams);
            }
            connectionString = match.Value;
        }

        var conn = new SqlConnection(connectionString);
        try
        {
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
