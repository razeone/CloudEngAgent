using Microsoft.Data.SqlClient;

namespace CloudEngAgent.Mcp.Server.Sql;

/// <summary>
/// Resolves a logical database name (as configured in
/// <c>Mcp:SqlServer:ConnectionStrings</c>) to an open
/// <see cref="SqlConnection"/>. Centralized to keep tools unit-testable
/// and to enforce a single connection-creation path.
/// </summary>
public interface ISqlConnectionFactory
{
    /// <summary>
    /// The set of configured logical database names, in declaration order.
    /// </summary>
    IReadOnlyList<string> ConfiguredDatabases { get; }

    /// <summary>
    /// Opens a new <see cref="SqlConnection"/> for the named logical database.
    /// If <paramref name="database"/> is null/empty the first configured
    /// database is used.
    /// </summary>
    Task<SqlConnection> OpenAsync(string? database, CancellationToken cancellationToken);
}
