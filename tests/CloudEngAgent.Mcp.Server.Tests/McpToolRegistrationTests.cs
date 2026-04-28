using System.Reflection;
using CloudEngAgent.Mcp.Server.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using Xunit;

namespace CloudEngAgent.Mcp.Server.Tests;

/// <summary>
/// Validates that all 7 new MVP demo tools are exposed via the
/// <see cref="McpServerToolAttribute"/> mechanism the SDK discovers from the
/// assembly. Catches accidental rename / missing-attribute regressions
/// without needing a live SQL Server.
/// </summary>
public sealed class McpToolRegistrationTests
{
    [Theory]
    [InlineData("top_queries", typeof(TopQueriesTool))]
    [InlineData("missing_indexes", typeof(MissingIndexesTool))]
    [InlineData("wait_stats", typeof(WaitStatsTool))]
    [InlineData("blocking_sessions", typeof(BlockingSessionsTool))]
    [InlineData("fk_graph", typeof(FkGraphTool))]
    [InlineData("column_stats", typeof(ColumnStatsTool))]
    [InlineData("db_health_checks", typeof(DbHealthChecksTool))]
    public void Tool_IsAdvertisedWithExpectedName(string expectedName, Type toolType)
    {
        toolType.GetCustomAttribute<McpServerToolTypeAttribute>().Should().NotBeNull(
            "{0} must be marked as an MCP tool type", toolType.Name);

        var method = toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .SingleOrDefault(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);

        method.Should().NotBeNull("{0} must expose a single [McpServerTool] method", toolType.Name);

        var attr = method!.GetCustomAttribute<McpServerToolAttribute>()!;
        attr.Name.Should().Be(expectedName);
    }

    [Fact]
    public void All_ExpectedToolNames_AreDiscoverableInAssembly()
    {
        var expected = new[]
        {
            "top_queries", "missing_indexes", "wait_stats", "blocking_sessions",
            "fk_graph", "column_stats", "db_health_checks",
        };

        var discovered = typeof(SqlServerTools).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
            .Where(a => a is not null)
            .Select(a => a!.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in expected)
        {
            discovered.Should().Contain(name);
        }
    }
}
