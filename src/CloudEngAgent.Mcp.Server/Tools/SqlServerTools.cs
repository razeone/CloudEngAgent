using System.ComponentModel;
using CloudEngAgent.Mcp.Server.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CloudEngAgent.Mcp.Server.Tools;

/// <summary>
/// Read-only MCP tools that introspect a SQL Server database. Every tool
/// uses parameterized SQL for values; identifiers are allow-listed via
/// <c>INFORMATION_SCHEMA</c> before being bracket-quoted with
/// <see cref="SqlIdentifier.Quote(string)"/>. SqlExceptions are logged with
/// full detail but only the SQL <c>Number</c> + a friendly summary is
/// returned to the caller (no stack traces leak through MCP).
/// </summary>
[McpServerToolType]
public sealed class SqlServerTools
{
    /// <summary>Hard cap for <c>sample_rows</c> regardless of caller request.</summary>
    public const int MaxSampleRows = 100;

    private readonly ISqlConnectionFactory _connections;
    private readonly ILogger<SqlServerTools> _logger;

    public SqlServerTools(ISqlConnectionFactory connections, ILogger<SqlServerTools> logger)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(logger);
        _connections = connections;
        _logger = logger;
    }

    [McpServerTool(Name = "list_databases")]
    [Description("Lists user databases on the configured SQL Server instance (system databases are excluded).")]
    public async Task<IReadOnlyList<string>> ListDatabasesAsync(
        [Description("Logical database name from configuration. Optional; defaults to the first configured entry.")]
        string? database = null,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenAsync(database, cancellationToken).ConfigureAwait(false);

        const string sql = """
            SELECT name
            FROM sys.databases
            WHERE database_id > 4
              AND state_desc = 'ONLINE'
              AND HAS_DBACCESS(name) = 1
            ORDER BY name;
            """;

        try
        {
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var results = new List<string>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(reader.GetString(0));
            }
            _logger.LogInformation(
                "list_databases returned {RowCount} databases for {Database}",
                results.Count, database ?? "<default>");
            return results;
        }
        catch (SqlException ex)
        {
            throw ToMcpException(ex, "list_databases");
        }
    }

    [McpServerTool(Name = "list_tables")]
    [Description("Lists schema-qualified user tables in the connected database via INFORMATION_SCHEMA.TABLES.")]
    public async Task<IReadOnlyList<TableRef>> ListTablesAsync(
        [Description("Logical database name from configuration. Optional; defaults to the first configured entry.")]
        string? database = null,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenAsync(database, cancellationToken).ConfigureAwait(false);

        const string sql = """
            SELECT TABLE_SCHEMA, TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_SCHEMA, TABLE_NAME;
            """;

        try
        {
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var results = new List<TableRef>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new TableRef(reader.GetString(0), reader.GetString(1)));
            }
            _logger.LogInformation(
                "list_tables returned {RowCount} tables for {Database}",
                results.Count, database ?? "<default>");
            return results;
        }
        catch (SqlException ex)
        {
            throw ToMcpException(ex, "list_tables");
        }
    }

    [McpServerTool(Name = "describe_table")]
    [Description("Returns the columns and SQL types of a table from INFORMATION_SCHEMA.COLUMNS.")]
    public async Task<IReadOnlyList<ColumnInfo>> DescribeTableAsync(
        [Description("Schema name (e.g., 'dbo').")] string schema,
        [Description("Table name.")] string table,
        [Description("Logical database name from configuration. Optional; defaults to the first configured entry.")]
        string? database = null,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(schema, nameof(schema));
        EnsureIdentifier(table, nameof(table));

        await using var conn = await OpenAsync(database, cancellationToken).ConfigureAwait(false);

        const string sql = """
            SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE,
                   CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE,
                   ORDINAL_POSITION
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION;
            """;

        try
        {
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@schema", System.Data.SqlDbType.NVarChar, 128) { Value = schema });
            cmd.Parameters.Add(new SqlParameter("@table", System.Data.SqlDbType.NVarChar, 128) { Value = table });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var results = new List<ColumnInfo>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new ColumnInfo(
                    Name: reader.GetString(0),
                    DataType: reader.GetString(1),
                    IsNullable: string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase),
                    MaxLength: reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    NumericPrecision: reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4), System.Globalization.CultureInfo.InvariantCulture),
                    NumericScale: reader.IsDBNull(5) ? null : Convert.ToInt32(reader.GetValue(5), System.Globalization.CultureInfo.InvariantCulture),
                    OrdinalPosition: reader.GetInt32(6)));
            }

            if (results.Count == 0)
            {
                throw new McpException(
                    $"Table [{schema}].[{table}] does not exist or has no columns visible to the connection.",
                    McpErrorCode.InvalidParams);
            }

            _logger.LogInformation(
                "describe_table returned {RowCount} columns for {Schema}.{Table} on {Database}",
                results.Count, schema, table, database ?? "<default>");
            return results;
        }
        catch (SqlException ex)
        {
            throw ToMcpException(ex, "describe_table");
        }
    }

    [McpServerTool(Name = "sample_rows")]
    [Description("Returns up to N rows from a table as a list of column→value dictionaries. N is capped at 100.")]
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SampleRowsAsync(
        [Description("Schema name (e.g., 'dbo').")] string schema,
        [Description("Table name.")] string table,
        [Description("Maximum number of rows to return. Capped at 100.")] int top = 10,
        [Description("Logical database name from configuration. Optional; defaults to the first configured entry.")]
        string? database = null,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(schema, nameof(schema));
        EnsureIdentifier(table, nameof(table));

        if (top <= 0)
        {
            throw new McpException("'top' must be greater than zero.", McpErrorCode.InvalidParams);
        }
        var capped = Math.Min(top, MaxSampleRows);

        await using var conn = await OpenAsync(database, cancellationToken).ConfigureAwait(false);

        if (!await TableExistsAsync(conn, schema, table, cancellationToken).ConfigureAwait(false))
        {
            throw new McpException(
                $"Table [{schema}].[{table}] does not exist or is not visible to the connection.",
                McpErrorCode.InvalidParams);
        }

        var quoted = SqlIdentifier.Quote(schema) + "." + SqlIdentifier.Quote(table);
        var sql = $"SELECT TOP ({capped.ToString(System.Globalization.CultureInfo.InvariantCulture)}) * FROM {quoted};";

        try
        {
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            var fieldCount = reader.FieldCount;
            var columnNames = new string[fieldCount];
            for (var i = 0; i < fieldCount; i++)
            {
                columnNames[i] = reader.GetName(i);
            }

            var results = new List<IReadOnlyDictionary<string, object?>>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var row = new Dictionary<string, object?>(fieldCount, StringComparer.Ordinal);
                for (var i = 0; i < fieldCount; i++)
                {
                    row[columnNames[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                results.Add(row);
            }

            _logger.LogInformation(
                "sample_rows returned {RowCount} rows from {Schema}.{Table} on {Database} (requested {Top}, capped {Capped})",
                results.Count, schema, table, database ?? "<default>", top, capped);
            return results;
        }
        catch (SqlException ex)
        {
            throw ToMcpException(ex, "sample_rows");
        }
    }

    private async Task<SqlConnection> OpenAsync(string? database, CancellationToken cancellationToken)
    {
        try
        {
            return await _connections.OpenAsync(database, cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            throw ToMcpException(ex, "open_connection");
        }
    }

    private static async Task<bool> TableExistsAsync(
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

    private static void EnsureIdentifier(string value, string paramName)
    {
        if (!SqlIdentifier.IsValid(value))
        {
            throw new McpException(
                $"Invalid value for '{paramName}': only ASCII letters, digits, and underscores are allowed (must start with a letter or underscore).",
                McpErrorCode.InvalidParams);
        }
    }

    private McpException ToMcpException(SqlException ex, string toolName)
    {
        _logger.LogError(ex,
            "MCP SQL tool {Tool} failed (Number={SqlNumber}, State={SqlState})",
            toolName, ex.Number, ex.State);
        return new McpException(
            $"SQL Server error {ex.Number}: {ex.Message.Split('\n')[0]}",
            McpErrorCode.InternalError);
    }

    public sealed record TableRef(string Schema, string Name);

    public sealed record ColumnInfo(
        string Name,
        string DataType,
        bool IsNullable,
        int? MaxLength,
        int? NumericPrecision,
        int? NumericScale,
        int OrdinalPosition);
}
