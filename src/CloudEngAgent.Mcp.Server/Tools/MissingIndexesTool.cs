using System.ComponentModel;
using CloudEngAgent.Mcp.Server.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CloudEngAgent.Mcp.Server.Tools;

[McpServerToolType]
public sealed class MissingIndexesTool
{
    public const int HardCap = 50;

    private readonly ISqlConnectionFactory _connections;
    private readonly ILogger<MissingIndexesTool> _logger;

    public MissingIndexesTool(ISqlConnectionFactory connections, ILogger<MissingIndexesTool> logger)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(logger);
        _connections = connections;
        _logger = logger;
    }

    [McpServerTool(Name = "missing_indexes")]
    [Description("Returns missing-index recommendations for the connected database from the sys.dm_db_missing_index_* DMVs, ordered by improvement_measure (top 50).")]
    public async Task<IReadOnlyList<MissingIndexRow>> InvokeAsync(
        [Description("Logical database name from configuration. Optional; defaults to the first configured entry.")]
        string? database = null,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await SqlToolHelpers.OpenAsync(_connections, _logger, database, "missing_indexes", cancellationToken).ConfigureAwait(false);

        const string sql = """
            SELECT TOP (50)
                OBJECT_SCHEMA_NAME(mid.object_id) AS schema_name,
                OBJECT_NAME(mid.object_id) AS table_name,
                ISNULL(mid.equality_columns, '') AS equality_columns,
                ISNULL(mid.inequality_columns, '') AS inequality_columns,
                ISNULL(mid.included_columns, '') AS included_columns,
                (migs.avg_total_user_cost * migs.avg_user_impact * (migs.user_seeks + migs.user_scans)) AS improvement_measure,
                migs.unique_compiles,
                migs.user_seeks,
                migs.user_scans
            FROM sys.dm_db_missing_index_details AS mid
            INNER JOIN sys.dm_db_missing_index_groups AS mig
                ON mid.index_handle = mig.index_handle
            INNER JOIN sys.dm_db_missing_index_group_stats AS migs
                ON mig.index_group_handle = migs.group_handle
            WHERE mid.database_id = DB_ID()
            ORDER BY improvement_measure DESC;
            """;

        try
        {
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var results = new List<MissingIndexRow>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new MissingIndexRow(
                    SchemaName: reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    TableName: reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    EqualityColumns: reader.GetString(2),
                    InequalityColumns: reader.GetString(3),
                    IncludedColumns: reader.GetString(4),
                    ImprovementMeasure: reader.IsDBNull(5) ? 0d : Convert.ToDouble(reader.GetValue(5), System.Globalization.CultureInfo.InvariantCulture),
                    UniqueCompiles: reader.IsDBNull(6) ? 0 : reader.GetInt64(6),
                    UserSeeks: reader.IsDBNull(7) ? 0 : reader.GetInt64(7),
                    UserScans: reader.IsDBNull(8) ? 0 : reader.GetInt64(8)));
            }
            _logger.LogInformation(
                "missing_indexes returned {RowCount} rows for {Database}",
                results.Count, database ?? "<default>");
            return results;
        }
        catch (SqlException ex)
        {
            throw SqlToolHelpers.ToMcpException(_logger, ex, "missing_indexes");
        }
    }

    public sealed record MissingIndexRow(
        string SchemaName,
        string TableName,
        string EqualityColumns,
        string InequalityColumns,
        string IncludedColumns,
        double ImprovementMeasure,
        long UniqueCompiles,
        long UserSeeks,
        long UserScans);
}
