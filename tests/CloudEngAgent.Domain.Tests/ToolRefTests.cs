using CloudEngAgent.Domain.Tools;
using FluentAssertions;
using Xunit;

namespace CloudEngAgent.Domain.Tests;

public class ToolRefTests
{
    [Fact]
    public void Parse_splits_server_and_tool()
    {
        var toolRef = ToolRef.Parse("mcp:cloudeng-db.sql.queryReadonly");

        toolRef.ServerName.Should().Be("cloudeng-db");
        toolRef.ToolName.Should().Be("sql.queryReadonly");
        toolRef.Qualified.Should().Be("mcp:cloudeng-db.sql.queryReadonly");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sql.queryReadonly")]
    [InlineData("mcp:")]
    [InlineData("mcp:server")]
    [InlineData("mcp:server.")]
    [InlineData("mcp:.tool")]
    public void TryParse_returns_false_for_invalid(string? value)
    {
        var ok = ToolRef.TryParse(value, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void Parse_throws_for_invalid()
    {
        var act = () => ToolRef.Parse("bad");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Equality_is_by_value()
    {
        var a = ToolRef.Parse("mcp:db.query");
        var b = ToolRef.Parse("mcp:db.query");

        a.Should().Be(b);
    }
}
