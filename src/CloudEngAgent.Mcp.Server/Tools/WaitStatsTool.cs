using System.ComponentModel;
using System.Globalization;
using CloudEngAgent.Mcp.Server.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CloudEngAgent.Mcp.Server.Tools;

[McpServerToolType]
public sealed class WaitStatsTool
{
    public const int HardCap = 50;
    public const int DefaultTop = 20;

    // Standard "noise" wait types excluded from server health analysis.
    // The values are SQL Server constants, not user input, so a literal
    // IN-list is safe.
    private const string ExcludedWaitsClause = """
        wait_type NOT LIKE 'CLR_%'
        AND wait_type NOT LIKE 'DBMIRROR_%'
        AND wait_type NOT LIKE 'BROKER_%'
        AND wait_type NOT LIKE 'HADR_%'
        AND wait_type NOT LIKE 'XE_%'
        AND wait_type NOT LIKE 'SLEEP_%'
        AND wait_type NOT IN (
            'SP_SERVER_DIAGNOSTICS_SLEEP',
            'LAZYWRITER_SLEEP',
            'REQUEST_FOR_DEADLOCK_SEARCH',
            'LOGMGR_QUEUE',
            'CHECKPOINT_QUEUE',
            'ONDEMAND_TASK_QUEUE',
            'FT_IFTS_SCHEDULER_IDLE_WAIT',
            'WAITFOR',
            'CLR_AUTO_EVENT',
            'CLR_MANUAL_EVENT',
            'DISPATCHER_QUEUE_SEMAPHORE',
            'FT_IFTSHC_MUTEX',
            'BROKER_RECEIVE_WAITFOR',
            'BROKER_TASK_STOP',
            'BROKER_TO_FLUSH',
            'MISCELLANEOUS'
        )
        """;

    private readonly ISqlConnectionFactory _connections;
    private readonly ILogger<WaitStatsTool> _logger;

    public WaitStatsTool(ISqlConnectionFactory connections, ILogger<WaitStatsTool> logger)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(logger);
        _connections = connections;
        _logger = logger;
    }

    [McpServerTool(Name = "wait_stats")]
    [Description("Returns top wait types from sys.dm_os_wait_stats with the standard idle/system wait noise filtered out. Capped at 50.")]
    public async Task<IReadOnlyList<WaitStatRow>> InvokeAsync(
        [Description("Logical database name from configuration. Optional; defaults to the first configured entry.")]
        string? database = null,
        [Description("Maximum number of wait types to return (1-50). Default 20.")]
        int top = DefaultTop,
        CancellationToken cancellationToken = default)
    {
        if (top <= 0)
        {
            throw new McpException("'top' must be greater than zero.", McpErrorCode.InvalidParams);
        }
        var capped = Math.Min(top, HardCap);

        await using var conn = await SqlToolHelpers.OpenAsync(_connections, _logger, database, "wait_stats", cancellationToken).ConfigureAwait(false);

        var sql = $"""
            WITH filtered AS (
                SELECT wait_type, waiting_tasks_count, wait_time_ms, signal_wait_time_ms
                FROM sys.dm_os_wait_stats
                WHERE {ExcludedWaitsClause}
                  AND wait_time_ms > 0
            )
            SELECT TOP ({capped.ToString(CultureInfo.InvariantCulture)})
                wait_type,
                waiting_tasks_count,
                wait_time_ms,
                signal_wait_time_ms,
                CASE WHEN SUM(wait_time_ms) OVER () = 0 THEN 0
                     ELSE 100.0 * wait_time_ms / SUM(wait_time_ms) OVER ()
                END AS pct_of_total
            FROM filtered
            ORDER BY wait_time_ms DESC;
            """;

        try
        {
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var results = new List<WaitStatRow>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new WaitStatRow(
                    WaitType: reader.GetString(0),
                    WaitingTasksCount: reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                    WaitTimeMs: reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                    SignalWaitTimeMs: reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
                    PctOfTotal: reader.IsDBNull(4) ? 0d : Convert.ToDouble(reader.GetValue(4), CultureInfo.InvariantCulture)));
            }
            _logger.LogInformation(
                "wait_stats returned {RowCount} rows for {Database} (requested {Top}, capped {Capped})",
                results.Count, database ?? "<default>", top, capped);
            return results;
        }
        catch (SqlException ex)
        {
            throw SqlToolHelpers.ToMcpException(_logger, ex, "wait_stats");
        }
    }

    public sealed record WaitStatRow(
        string WaitType,
        long WaitingTasksCount,
        long WaitTimeMs,
        long SignalWaitTimeMs,
        double PctOfTotal);
}
