using System.ComponentModel;
using CloudEngAgent.Mcp.Server.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CloudEngAgent.Mcp.Server.Tools;

[McpServerToolType]
public sealed class BlockingSessionsTool
{
    private readonly ISqlConnectionFactory _connections;
    private readonly ILogger<BlockingSessionsTool> _logger;

    public BlockingSessionsTool(ISqlConnectionFactory connections, ILogger<BlockingSessionsTool> logger)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(logger);
        _connections = connections;
        _logger = logger;
    }

    [McpServerTool(Name = "blocking_sessions")]
    [Description("Returns currently blocked sessions (blocking_session_id <> 0) joined to blocker and blocked sessions and the blocked SQL text (first 500 chars).")]
    public async Task<IReadOnlyList<BlockingSessionRow>> InvokeAsync(
        [Description("Logical database name from configuration. Optional; defaults to the first configured entry.")]
        string? database = null,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await SqlToolHelpers.OpenAsync(_connections, _logger, database, "blocking_sessions", cancellationToken).ConfigureAwait(false);

        const string sql = """
            SELECT
                blocked.session_id  AS blocked_session_id,
                blocked.blocking_session_id AS blocking_session_id,
                blocked.wait_type,
                blocked.wait_time   AS wait_time_ms,
                blocked.wait_resource,
                bs.login_name       AS blocked_login,
                gs.login_name       AS blocking_login,
                SUBSTRING(t.text, 1, 500) AS blocked_query
            FROM sys.dm_exec_requests AS blocked
            INNER JOIN sys.dm_exec_sessions AS bs
                ON blocked.session_id = bs.session_id
            LEFT JOIN sys.dm_exec_sessions AS gs
                ON blocked.blocking_session_id = gs.session_id
            OUTER APPLY sys.dm_exec_sql_text(blocked.sql_handle) AS t
            WHERE blocked.blocking_session_id <> 0;
            """;

        try
        {
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var results = new List<BlockingSessionRow>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new BlockingSessionRow(
                    BlockedSessionId: reader.IsDBNull(0) ? 0 : (int)reader.GetInt16(0),
                    BlockingSessionId: reader.IsDBNull(1) ? 0 : (int)reader.GetInt16(1),
                    WaitType: reader.IsDBNull(2) ? null : reader.GetString(2),
                    WaitTimeMs: reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    WaitResource: reader.IsDBNull(4) ? null : reader.GetString(4),
                    BlockedLogin: reader.IsDBNull(5) ? null : reader.GetString(5),
                    BlockingLogin: reader.IsDBNull(6) ? null : reader.GetString(6),
                    BlockedQuery: reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
            _logger.LogInformation(
                "blocking_sessions returned {RowCount} rows for {Database}",
                results.Count, database ?? "<default>");
            return results;
        }
        catch (SqlException ex)
        {
            throw SqlToolHelpers.ToMcpException(_logger, ex, "blocking_sessions");
        }
    }

    public sealed record BlockingSessionRow(
        int BlockedSessionId,
        int BlockingSessionId,
        string? WaitType,
        int WaitTimeMs,
        string? WaitResource,
        string? BlockedLogin,
        string? BlockingLogin,
        string? BlockedQuery);
}
