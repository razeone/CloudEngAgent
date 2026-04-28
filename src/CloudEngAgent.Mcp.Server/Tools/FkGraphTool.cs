using System.ComponentModel;
using CloudEngAgent.Mcp.Server.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CloudEngAgent.Mcp.Server.Tools;

[McpServerToolType]
public sealed class FkGraphTool
{
    private readonly ISqlConnectionFactory _connections;
    private readonly ILogger<FkGraphTool> _logger;

    public FkGraphTool(ISqlConnectionFactory connections, ILogger<FkGraphTool> logger)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(logger);
        _connections = connections;
        _logger = logger;
    }

    [McpServerTool(Name = "fk_graph")]
    [Description("Returns the foreign-key graph of the connected database (one row per FK column; composite FKs produce multiple rows). No row cap — can return hundreds.")]
    public async Task<IReadOnlyList<FkEdge>> InvokeAsync(
        [Description("Logical database name from configuration. Optional; defaults to the first configured entry.")]
        string? database = null,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await SqlToolHelpers.OpenAsync(_connections, _logger, database, "fk_graph", cancellationToken).ConfigureAwait(false);

        const string sql = """
            SELECT
                fk.name AS fk_name,
                ps.name AS parent_schema,
                pt.name AS parent_table,
                pc.name AS parent_column,
                rs.name AS referenced_schema,
                rt.name AS referenced_table,
                rc.name AS referenced_column,
                fk.is_disabled,
                fk.delete_referential_action_desc
            FROM sys.foreign_keys AS fk
            INNER JOIN sys.foreign_key_columns AS fkc
                ON fkc.constraint_object_id = fk.object_id
            INNER JOIN sys.tables   AS pt ON pt.object_id = fk.parent_object_id
            INNER JOIN sys.schemas  AS ps ON ps.schema_id = pt.schema_id
            INNER JOIN sys.columns  AS pc ON pc.object_id = fk.parent_object_id AND pc.column_id = fkc.parent_column_id
            INNER JOIN sys.tables   AS rt ON rt.object_id = fk.referenced_object_id
            INNER JOIN sys.schemas  AS rs ON rs.schema_id = rt.schema_id
            INNER JOIN sys.columns  AS rc ON rc.object_id = fk.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            ORDER BY ps.name, pt.name, fk.name, fkc.constraint_column_id;
            """;

        try
        {
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var results = new List<FkEdge>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new FkEdge(
                    FkName: reader.GetString(0),
                    ParentSchema: reader.GetString(1),
                    ParentTable: reader.GetString(2),
                    ParentColumn: reader.GetString(3),
                    ReferencedSchema: reader.GetString(4),
                    ReferencedTable: reader.GetString(5),
                    ReferencedColumn: reader.GetString(6),
                    IsDisabled: !reader.IsDBNull(7) && reader.GetBoolean(7),
                    DeleteReferentialActionDesc: reader.IsDBNull(8) ? string.Empty : reader.GetString(8)));
            }
            _logger.LogInformation(
                "fk_graph returned {RowCount} rows for {Database}",
                results.Count, database ?? "<default>");
            return results;
        }
        catch (SqlException ex)
        {
            throw SqlToolHelpers.ToMcpException(_logger, ex, "fk_graph");
        }
    }

    public sealed record FkEdge(
        string FkName,
        string ParentSchema,
        string ParentTable,
        string ParentColumn,
        string ReferencedSchema,
        string ReferencedTable,
        string ReferencedColumn,
        bool IsDisabled,
        string DeleteReferentialActionDesc);
}
