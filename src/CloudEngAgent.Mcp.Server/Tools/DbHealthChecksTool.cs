using System.ComponentModel;
using System.Globalization;
using CloudEngAgent.Mcp.Server.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CloudEngAgent.Mcp.Server.Tools;

[McpServerToolType]
public sealed class DbHealthChecksTool
{
    private readonly ISqlConnectionFactory _connections;
    private readonly ILogger<DbHealthChecksTool> _logger;

    public DbHealthChecksTool(ISqlConnectionFactory connections, ILogger<DbHealthChecksTool> logger)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(logger);
        _connections = connections;
        _logger = logger;
    }

    [McpServerTool(Name = "db_health_checks")]
    [Description("Runs a curated set of read-only health checks against the connected database and returns one row per check (severity, category, pass/fail, details, remediation).")]
    public async Task<IReadOnlyList<HealthCheckRow>> InvokeAsync(
        [Description("Logical database name from configuration. Optional; defaults to the first configured entry.")]
        string? database = null,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await SqlToolHelpers.OpenAsync(_connections, _logger, database, "db_health_checks", cancellationToken).ConfigureAwait(false);

        var results = new List<HealthCheckRow>();

        // Single round-trip pulls the database-level config flags we need.
        DbConfig? dbInfo;
        try
        {
            const string cfgSql = """
                SELECT
                    is_auto_close_on,
                    is_auto_shrink_on,
                    recovery_model_desc,
                    page_verify_option_desc,
                    compatibility_level,
                    name
                FROM sys.databases
                WHERE database_id = DB_ID();
                """;
            await using var cmd = new SqlCommand(cfgSql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            dbInfo = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? new DbConfig(
                    AutoClose: reader.GetBoolean(0),
                    AutoShrink: reader.GetBoolean(1),
                    RecoveryModel: reader.GetString(2),
                    PageVerify: reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    CompatibilityLevel: reader.GetByte(4),
                    DbName: reader.GetString(5))
                : null;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex,
                "db_health_checks: prequery failed (Number={SqlNumber}, State={SqlState})",
                ex.Number, ex.State);
            dbInfo = null;
        }

        if (dbInfo is null)
        {
            results.Add(new HealthCheckRow(
                "auto_close_off", "Info", "Configuration", false,
                "AUTO_CLOSE is OFF",
                "Could not read sys.databases for current DB.",
                "Verify connection has access to sys.databases."));
            return results;
        }

        var info = dbInfo.Value;

        results.Add(new HealthCheckRow(
            CheckId: "auto_close_off",
            Severity: "Medium",
            Category: "Configuration",
            Passed: !info.AutoClose,
            Title: "AUTO_CLOSE is OFF",
            Details: info.AutoClose
                ? "is_auto_close_on = 1 — DB closes/reopens on idle, hurting first-hit latency."
                : "is_auto_close_on = 0.",
            Remediation: info.AutoClose ? $"ALTER DATABASE [{info.DbName}] SET AUTO_CLOSE OFF;" : string.Empty));

        results.Add(new HealthCheckRow(
            CheckId: "auto_shrink_off",
            Severity: "High",
            Category: "Performance",
            Passed: !info.AutoShrink,
            Title: "AUTO_SHRINK is OFF",
            Details: info.AutoShrink
                ? "is_auto_shrink_on = 1 — auto shrink causes fragmentation and CPU spikes."
                : "is_auto_shrink_on = 0.",
            Remediation: info.AutoShrink ? $"ALTER DATABASE [{info.DbName}] SET AUTO_SHRINK OFF;" : string.Empty));

        var recoveryAppropriate =
            string.Equals(info.RecoveryModel, "FULL", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(info.RecoveryModel, "SIMPLE", StringComparison.OrdinalIgnoreCase);
        results.Add(new HealthCheckRow(
            CheckId: "recovery_model_appropriate",
            Severity: "Info",
            Category: "Operational",
            Passed: recoveryAppropriate,
            Title: "Recovery model is FULL or SIMPLE",
            Details: $"recovery_model_desc = '{info.RecoveryModel}'.",
            Remediation: recoveryAppropriate
                ? string.Empty
                : $"Consider ALTER DATABASE [{info.DbName}] SET RECOVERY FULL; (or SIMPLE)."));

        var pageVerifyOk = string.Equals(info.PageVerify, "CHECKSUM", StringComparison.OrdinalIgnoreCase);
        results.Add(new HealthCheckRow(
            CheckId: "page_verify_checksum",
            Severity: "High",
            Category: "Operational",
            Passed: pageVerifyOk,
            Title: "PAGE_VERIFY is CHECKSUM",
            Details: $"page_verify_option_desc = '{info.PageVerify}'.",
            Remediation: pageVerifyOk ? string.Empty : $"ALTER DATABASE [{info.DbName}] SET PAGE_VERIFY CHECKSUM;"));

        var compatOk = info.CompatibilityLevel >= 140;
        results.Add(new HealthCheckRow(
            CheckId: "compatibility_level_modern",
            Severity: "Medium",
            Category: "Configuration",
            Passed: compatOk,
            Title: "Compatibility level >= 140",
            Details: $"compatibility_level = {info.CompatibilityLevel.ToString(CultureInfo.InvariantCulture)}.",
            Remediation: compatOk
                ? string.Empty
                : $"ALTER DATABASE [{info.DbName}] SET COMPATIBILITY_LEVEL = 150; (or higher per supported)."));

        results.Add(await CheckQueryStoreAsync(conn, info.DbName, cancellationToken).ConfigureAwait(false));
        results.Add(await CheckLastFullBackupAsync(conn, info.DbName, cancellationToken).ConfigureAwait(false));
        results.Add(await CheckLastLogBackupAsync(conn, info.DbName, info.RecoveryModel, cancellationToken).ConfigureAwait(false));
        results.Add(await CheckUnusedIndexesAsync(conn, cancellationToken).ConfigureAwait(false));
        results.Add(await CheckDbccCheckDbAsync(conn, info.DbName, cancellationToken).ConfigureAwait(false));

        _logger.LogInformation(
            "db_health_checks returned {RowCount} checks for {Database}",
            results.Count, database ?? "<default>");
        return results;
    }

    private async Task<HealthCheckRow> CheckQueryStoreAsync(SqlConnection conn, string dbName, CancellationToken ct)
    {
        try
        {
            const string sql = "SELECT actual_state_desc FROM sys.database_query_store_options;";
            await using var cmd = new SqlCommand(sql, conn);
            var state = (string?)await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? "OFF";
            var passed = !string.Equals(state, "OFF", StringComparison.OrdinalIgnoreCase);
            return new HealthCheckRow(
                "query_store_enabled", "Medium", "Performance", passed,
                "Query Store is enabled",
                $"actual_state_desc = '{state}'.",
                passed ? string.Empty : $"ALTER DATABASE [{dbName}] SET QUERY_STORE = ON;");
        }
        catch (SqlException ex)
        {
            return ToErrorRow("query_store_enabled", "Performance", "Query Store is enabled", ex);
        }
    }

    private async Task<HealthCheckRow> CheckLastFullBackupAsync(SqlConnection conn, string dbName, CancellationToken ct)
    {
        try
        {
            const string sql = """
                SELECT TOP (1) backup_finish_date
                FROM msdb.dbo.backupset
                WHERE database_name = @db AND type = 'D'
                ORDER BY backup_finish_date DESC;
                """;
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@db", System.Data.SqlDbType.NVarChar, 128) { Value = dbName });
            var raw = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (raw is null or DBNull)
            {
                return new HealthCheckRow(
                    "last_full_backup_within_7d", "Critical", "Operational", false,
                    "Full backup within 7 days",
                    "No full backup recorded in msdb.dbo.backupset.",
                    $"BACKUP DATABASE [{dbName}] TO DISK = N'...';");
            }
            var when = (DateTime)raw;
            var ageDays = (DateTime.UtcNow - when.ToUniversalTime()).TotalDays;
            var passed = ageDays <= 7;
            return new HealthCheckRow(
                "last_full_backup_within_7d", "Critical", "Operational", passed,
                "Full backup within 7 days",
                $"Last full backup at {when:O} ({ageDays.ToString("F1", CultureInfo.InvariantCulture)} days ago).",
                passed ? string.Empty : $"BACKUP DATABASE [{dbName}] TO DISK = N'...';");
        }
        catch (SqlException ex)
        {
            return SkippedRow("last_full_backup_within_7d", "Operational", "Full backup within 7 days", ex);
        }
    }

    private async Task<HealthCheckRow> CheckLastLogBackupAsync(SqlConnection conn, string dbName, string recoveryModel, CancellationToken ct)
    {
        if (!string.Equals(recoveryModel, "FULL", StringComparison.OrdinalIgnoreCase))
        {
            return new HealthCheckRow(
                "last_log_backup_within_24h_when_full_recovery", "Info", "Operational", true,
                "Log backup within 24h (FULL recovery only)",
                $"Recovery model is '{recoveryModel}' — log-backup check skipped.",
                string.Empty);
        }

        try
        {
            const string sql = """
                SELECT TOP (1) backup_finish_date
                FROM msdb.dbo.backupset
                WHERE database_name = @db AND type = 'L'
                ORDER BY backup_finish_date DESC;
                """;
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@db", System.Data.SqlDbType.NVarChar, 128) { Value = dbName });
            var raw = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (raw is null or DBNull)
            {
                return new HealthCheckRow(
                    "last_log_backup_within_24h_when_full_recovery", "Critical", "Operational", false,
                    "Log backup within 24h (FULL recovery)",
                    "No log backup recorded in msdb.dbo.backupset.",
                    $"BACKUP LOG [{dbName}] TO DISK = N'...';");
            }
            var when = (DateTime)raw;
            var ageHours = (DateTime.UtcNow - when.ToUniversalTime()).TotalHours;
            var passed = ageHours <= 24;
            return new HealthCheckRow(
                "last_log_backup_within_24h_when_full_recovery", "Critical", "Operational", passed,
                "Log backup within 24h (FULL recovery)",
                $"Last log backup at {when:O} ({ageHours.ToString("F1", CultureInfo.InvariantCulture)} hours ago).",
                passed ? string.Empty : $"BACKUP LOG [{dbName}] TO DISK = N'...';");
        }
        catch (SqlException ex)
        {
            return SkippedRow("last_log_backup_within_24h_when_full_recovery", "Operational", "Log backup within 24h (FULL recovery)", ex);
        }
    }

    private async Task<HealthCheckRow> CheckUnusedIndexesAsync(SqlConnection conn, CancellationToken ct)
    {
        try
        {
            const string sql = """
                SELECT COUNT(*)
                FROM sys.dm_db_index_usage_stats AS ius
                INNER JOIN sys.indexes AS i
                    ON ius.object_id = i.object_id AND ius.index_id = i.index_id
                WHERE ius.database_id = DB_ID()
                  AND i.index_id > 1
                  AND i.is_unique = 0
                  AND (ius.user_seeks + ius.user_scans + ius.user_lookups) = 0
                  AND ius.user_updates > 0;
                """;
            await using var cmd = new SqlCommand(sql, conn);
            var raw = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            var count = raw is int i ? i : Convert.ToInt32(raw, CultureInfo.InvariantCulture);
            var passed = count == 0;
            return new HealthCheckRow(
                "unused_indexes_present", "Low", "Performance", passed,
                "No unused indexes detected",
                $"{count.ToString(CultureInfo.InvariantCulture)} indexes appear unused (writes only, no reads since last restart).",
                passed
                    ? string.Empty
                    : "Review missing_indexes / index usage stats and consider DROPing unused non-unique non-clustered indexes.");
        }
        catch (SqlException ex)
        {
            return ToErrorRow("unused_indexes_present", "Performance", "No unused indexes detected", ex);
        }
    }

    private async Task<HealthCheckRow> CheckDbccCheckDbAsync(SqlConnection conn, string dbName, CancellationToken ct)
    {
        try
        {
            // DBCC DBINFO requires elevated permissions — falls back to "skipped" on access denied.
            var sql = $"DBCC DBINFO ({SqlIdentifier.Quote(dbName)}) WITH TABLERESULTS, NO_INFOMSGS;";
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            DateTime? lastKnownGood = null;
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                string? field = null;
                string? value = null;
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    if (string.Equals(name, "Field", StringComparison.OrdinalIgnoreCase))
                    {
                        field = reader.IsDBNull(i) ? null : reader.GetValue(i)?.ToString();
                    }
                    else if (string.Equals(name, "Value", StringComparison.OrdinalIgnoreCase))
                    {
                        value = reader.IsDBNull(i) ? null : reader.GetValue(i)?.ToString();
                    }
                }
                if (string.Equals(field, "dbi_dbccLastKnownGood", StringComparison.OrdinalIgnoreCase) &&
                    DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    lastKnownGood = parsed;
                    break;
                }
            }

            if (lastKnownGood is null || lastKnownGood.Value.Year <= 1900)
            {
                return new HealthCheckRow(
                    "dbcc_checkdb_recent", "High", "Operational", false,
                    "DBCC CHECKDB run within 7 days",
                    "No record of a successful DBCC CHECKDB.",
                    $"DBCC CHECKDB ([{dbName}]);");
            }

            var ageDays = (DateTime.UtcNow - lastKnownGood.Value.ToUniversalTime()).TotalDays;
            var passed = ageDays <= 7;
            return new HealthCheckRow(
                "dbcc_checkdb_recent", "High", "Operational", passed,
                "DBCC CHECKDB run within 7 days",
                $"dbi_dbccLastKnownGood = {lastKnownGood:O} ({ageDays.ToString("F1", CultureInfo.InvariantCulture)} days ago).",
                passed ? string.Empty : $"DBCC CHECKDB ([{dbName}]);");
        }
        catch (SqlException ex)
        {
            return SkippedRow("dbcc_checkdb_recent", "Operational", "DBCC CHECKDB run within 7 days", ex);
        }
    }

    private HealthCheckRow ToErrorRow(string id, string category, string title, SqlException ex)
    {
        _logger.LogError(ex,
            "db_health_checks: check {CheckId} failed (Number={SqlNumber}, State={SqlState})",
            id, ex.Number, ex.State);
        return new HealthCheckRow(
            id, "Info", category, false, title,
            $"error: {ex.Number} {SqlToolHelpers.FirstLine(ex.Message)}",
            string.Empty);
    }

    private HealthCheckRow SkippedRow(string id, string category, string title, SqlException ex)
    {
        _logger.LogWarning(ex,
            "db_health_checks: check {CheckId} skipped (Number={SqlNumber}, State={SqlState})",
            id, ex.Number, ex.State);
        return new HealthCheckRow(
            id, "Info", category, false, title,
            $"skipped — insufficient permissions or unavailable ({ex.Number}: {SqlToolHelpers.FirstLine(ex.Message)})",
            string.Empty);
    }

    private readonly record struct DbConfig(
        bool AutoClose,
        bool AutoShrink,
        string RecoveryModel,
        string PageVerify,
        byte CompatibilityLevel,
        string DbName);

    public sealed record HealthCheckRow(
        string CheckId,
        string Severity,
        string Category,
        bool Passed,
        string Title,
        string Details,
        string Remediation);
}
