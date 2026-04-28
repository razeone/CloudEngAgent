using System.ComponentModel;
using CloudEngAgent.Mcp.Server.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CloudEngAgent.Mcp.Server.Tools;

[McpServerToolType]
public sealed class ColumnStatsTool
{
    private readonly ISqlConnectionFactory _connections;
    private readonly ILogger<ColumnStatsTool> _logger;

    public ColumnStatsTool(ISqlConnectionFactory connections, ILogger<ColumnStatsTool> logger)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(logger);
        _connections = connections;
        _logger = logger;
    }

    [McpServerTool(Name = "column_stats")]
    [Description("Returns column statistics (sys.stats joined to sys.stats_columns + sys.dm_db_stats_properties) for a given table. Validates schema/table identifiers.")]
    public async Task<IReadOnlyList<ColumnStatRow>> InvokeAsync(
        [Description("Schema name (e.g., 'dbo').")] string schema,
        [Description("Table name.")] string table,
        [Description("Logical database name from configuration. Optional; defaults to the first configured entry.")]
        string? database = null,
        CancellationToken cancellationToken = default)
    {
        SqlToolHelpers.EnsureIdentifier(schema, nameof(schema));
        SqlToolHelpers.EnsureIdentifier(table, nameof(table));

        await using var conn = await SqlToolHelpers.OpenAsync(_connections, _logger, database, "column_stats", cancellationToken).ConfigureAwait(false);

        if (!await SqlToolHelpers.TableExistsAsync(conn, schema, table, cancellationToken).ConfigureAwait(false))
        {
            throw new McpException(
                $"Table [{schema}].[{table}] does not exist or is not visible to the connection.",
                McpErrorCode.InvalidParams);
        }

        // Quote AFTER existence check; identifiers were already allow-listed.
        var fullName = SqlIdentifier.Quote(schema) + "." + SqlIdentifier.Quote(table);

        // OBJECT_ID() takes a string literal — feed the qualified, allow-listed name as a parameter.
        var qualified = $"{schema}.{table}";

        const string sql = """
            DECLARE @oid int = OBJECT_ID(@qualified);
            SELECT
                c.name        AS column_name,
                s.name        AS stats_name,
                s.auto_created,
                s.user_created,
                sp.last_updated,
                sp.rows,
                sp.rows_sampled,
                sp.modification_counter,
                sp.unfiltered_rows
            FROM sys.stats AS s
            INNER JOIN sys.stats_columns AS sc
                ON sc.object_id = s.object_id AND sc.stats_id = s.stats_id
            INNER JOIN sys.columns AS c
                ON c.object_id = sc.object_id AND c.column_id = sc.column_id
            OUTER APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) AS sp
            WHERE s.object_id = @oid
            ORDER BY s.name, sc.stats_column_id;
            """;

        try
        {
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@qualified", System.Data.SqlDbType.NVarChar, 257) { Value = qualified });
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var results = new List<ColumnStatRow>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new ColumnStatRow(
                    ColumnName: reader.GetString(0),
                    StatsName: reader.GetString(1),
                    AutoCreated: !reader.IsDBNull(2) && reader.GetBoolean(2),
                    UserCreated: !reader.IsDBNull(3) && reader.GetBoolean(3),
                    LastUpdated: reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                    Rows: reader.IsDBNull(5) ? null : reader.GetInt64(5),
                    RowsSampled: reader.IsDBNull(6) ? null : reader.GetInt64(6),
                    ModificationCounter: reader.IsDBNull(7) ? null : reader.GetInt64(7),
                    UnfilteredRows: reader.IsDBNull(8) ? null : reader.GetInt64(8)));
            }
            _logger.LogInformation(
                "column_stats returned {RowCount} rows for {Schema}.{Table} on {Database} (resolved via {Qualified})",
                results.Count, schema, table, database ?? "<default>", fullName);
            return results;
        }
        catch (SqlException ex)
        {
            throw SqlToolHelpers.ToMcpException(_logger, ex, "column_stats");
        }
    }

    public sealed record ColumnStatRow(
        string ColumnName,
        string StatsName,
        bool AutoCreated,
        bool UserCreated,
        DateTime? LastUpdated,
        long? Rows,
        long? RowsSampled,
        long? ModificationCounter,
        long? UnfilteredRows);
}
