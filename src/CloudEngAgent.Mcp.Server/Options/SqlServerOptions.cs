namespace CloudEngAgent.Mcp.Server.Options;

/// <summary>
/// Bound from configuration section <c>Mcp:SqlServer</c>. Each entry in
/// <see cref="ConnectionStrings"/> maps a logical database name (the value
/// callers pass to MCP tools) → a SQL Server ADO.NET connection string.
/// The first entry is the default when a tool's <c>database</c> argument is
/// omitted or empty.
/// </summary>
public sealed class SqlServerOptions
{
    public const string SectionName = "Mcp:SqlServer";

    public Dictionary<string, string> ConnectionStrings { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}
