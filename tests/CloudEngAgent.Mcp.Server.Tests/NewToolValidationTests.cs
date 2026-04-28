using CloudEngAgent.Mcp.Server.Sql;
using CloudEngAgent.Mcp.Server.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using NSubstitute;
using Xunit;

namespace CloudEngAgent.Mcp.Server.Tests;

/// <summary>
/// Identifier-validation tests for the new MVP tools. These exercise the
/// pre-DB validation path so we can assert the InvalidParams shape without
/// needing a live SQL Server (parity with the existing test suite).
/// </summary>
public sealed class ColumnStatsToolValidationTests
{
    private static ColumnStatsTool CreateTool() =>
        new(Substitute.For<ISqlConnectionFactory>(), NullLogger<ColumnStatsTool>.Instance);

    [Theory]
    [InlineData("dbo';--", "Customers")]
    [InlineData("dbo", "Customers; DROP TABLE Customers--")]
    [InlineData("has space", "t")]
    [InlineData("dbo", "1startsWithDigit")]
    [InlineData("", "Customers")]
    [InlineData("dbo", "")]
    public async Task Invoke_RejectsInvalidIdentifierBeforeOpeningConnection(string schema, string table)
    {
        var tool = CreateTool();

        var act = async () => await tool.InvokeAsync(
            schema: schema,
            table: table,
            database: "anything",
            cancellationToken: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<McpException>();
        ex.Which.ErrorCode.Should().Be(McpErrorCode.InvalidParams);
    }
}

public sealed class TopQueriesToolValidationTests
{
    [Fact]
    public async Task Invoke_RejectsZeroOrNegativeTopBeforeOpeningConnection()
    {
        var tool = new TopQueriesTool(Substitute.For<ISqlConnectionFactory>(), NullLogger<TopQueriesTool>.Instance);

        var act = async () => await tool.InvokeAsync(top: 0, cancellationToken: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<McpException>();
        ex.Which.ErrorCode.Should().Be(McpErrorCode.InvalidParams);
    }
}

public sealed class WaitStatsToolValidationTests
{
    [Fact]
    public async Task Invoke_RejectsZeroOrNegativeTopBeforeOpeningConnection()
    {
        var tool = new WaitStatsTool(Substitute.For<ISqlConnectionFactory>(), NullLogger<WaitStatsTool>.Instance);

        var act = async () => await tool.InvokeAsync(top: -1, cancellationToken: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<McpException>();
        ex.Which.ErrorCode.Should().Be(McpErrorCode.InvalidParams);
    }
}
