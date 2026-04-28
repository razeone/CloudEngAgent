using System.ComponentModel;
using System.Globalization;
using CloudEngAgent.Mcp.Server.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CloudEngAgent.Mcp.Server.Tools;

[McpServerToolType]
public sealed class TopQueriesTool
{
    public const int HardCap = 50;
    public const int DefaultTop = 10;

    private readonly ISqlConnectionFactory _connections;
    private readonly ILogger<TopQueriesTool> _logger;

    public TopQueriesTool(ISqlConnectionFactory connections, ILogger<TopQueriesTool> logger)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(logger);
        _connections = connections;
        _logger = logger;
    }

    [McpServerTool(Name = "top_queries")]
    [Description("Returns the top-N queries by total CPU (worker time) from sys.dm_exec_query_stats. Read-only; capped at 50.")]
    public async Task<IReadOnlyList<TopQueryRow>> InvokeAsync(
        [Description("Logical database name from configuration. Optional; defaults to the first configured entry.")]
        string? database = null,
        [Description("Maximum number of queries to return (1-50). Default 10.")]
        int top = DefaultTop,
        CancellationToken cancellationToken = default)
    {
        if (top <= 0)
        {
            throw new McpException("'top' must be greater than zero.", McpErrorCode.InvalidParams);
        }
        var capped = Math.Min(top, HardCap);

        await using var conn = await SqlToolHelpers.OpenAsync(_connections, _logger, database, "top_queries", cancellationToken).ConfigureAwait(false);

        var sql = $"""
            SELECT TOP ({capped.ToString(CultureInfo.InvariantCulture)})
                SUBSTRING(t.text,
                    (qs.statement_start_offset / 2) + 1,
                    CASE WHEN qs.statement_end_offset = -1
                         THEN 250
                         ELSE (qs.statement_end_offset - qs.statement_start_offset) / 2
                    END) AS query_text,
                qs.execution_count,
                qs.total_worker_time / 1000.0 AS total_worker_time_ms,
                (qs.total_worker_time / NULLIF(qs.execution_count, 0)) / 1000.0 AS avg_worker_time_ms,
                qs.total_logical_reads,
                qs.total_logical_reads / NULLIF(qs.execution_count, 0) AS avg_logical_reads,
                qs.total_rows,
                qs.last_execution_time
            FROM sys.dm_exec_query_stats AS qs
            CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS t
            ORDER BY qs.total_worker_time DESC;
            """;

        try
        {
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var results = new List<TopQueryRow>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var raw = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var queryText = raw.Length > 500 ? raw[..500] : raw;
                results.Add(new TopQueryRow(
                    QueryText: queryText,
                    ExecutionCount: reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                    TotalWorkerTimeMs: reader.IsDBNull(2) ? 0d : Convert.ToDouble(reader.GetValue(2), CultureInfo.InvariantCulture),
                    AvgWorkerTimeMs: reader.IsDBNull(3) ? 0d : Convert.ToDouble(reader.GetValue(3), CultureInfo.InvariantCulture),
                    TotalLogicalReads: reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
                    AvgLogicalReads: reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
                    TotalRows: reader.IsDBNull(6) ? 0 : reader.GetInt64(6),
                    LastExecutionTime: reader.IsDBNull(7) ? null : reader.GetDateTime(7)));
            }
            _logger.LogInformation(
                "top_queries returned {RowCount} rows for {Database} (requested {Top}, capped {Capped})",
                results.Count, database ?? "<default>", top, capped);
            return results;
        }
        catch (SqlException ex)
        {
            throw SqlToolHelpers.ToMcpException(_logger, ex, "top_queries");
        }
    }

    public sealed record TopQueryRow(
        string QueryText,
        long ExecutionCount,
        double TotalWorkerTimeMs,
        double AvgWorkerTimeMs,
        long TotalLogicalReads,
        long AvgLogicalReads,
        long TotalRows,
        DateTime? LastExecutionTime);
}
