using CloudEngAgent.Mcp.Server.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace CloudEngAgent.Mcp.Server.Tools;

/// <summary>
/// Shared helpers for read-only SQL Server MCP tools. Centralizes the
/// identifier allow-list check, the SqlException → McpException mapping
/// (logs full detail, returns only Number + first line to the caller), and
/// the INFORMATION_SCHEMA existence probe used by tools that need to
/// bracket-quote a schema/table after parameterized validation.
/// </summary>
internal static class SqlToolHelpers
{
    public static void EnsureIdentifier(string value, string paramName)
    {
        if (!SqlIdentifier.IsValid(value))
        {
            throw new McpException(
                $"Invalid value for '{paramName}': only ASCII letters, digits, and underscores are allowed (must start with a letter or underscore).",
                McpErrorCode.InvalidParams);
        }
    }

    public static async Task<SqlConnection> OpenAsync(
        ISqlConnectionFactory connections,
        ILogger logger,
        string? database,
        string toolName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await connections.OpenAsync(database, cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            throw ToMcpException(logger, ex, toolName);
        }
    }

    public static McpException ToMcpException(ILogger logger, SqlException ex, string toolName)
    {
        logger.LogError(ex,
            "MCP SQL tool {Tool} failed (Number={SqlNumber}, State={SqlState})",
            toolName, ex.Number, ex.State);
        return new McpException(
            $"SQL Server error {ex.Number}: {ex.Message.Split('\n')[0]}",
            McpErrorCode.InternalError);
    }

    public static string FirstLine(string message) =>
        string.IsNullOrEmpty(message) ? string.Empty : message.Split('\n')[0];

    public static async Task<bool> TableExistsAsync(
        SqlConnection conn, string schema, string table, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT 1
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = @schema
              AND TABLE_NAME = @table
              AND TABLE_TYPE = 'BASE TABLE';
            """;
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@schema", System.Data.SqlDbType.NVarChar, 128) { Value = schema });
        cmd.Parameters.Add(new SqlParameter("@table", System.Data.SqlDbType.NVarChar, 128) { Value = table });
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null;
    }
}
