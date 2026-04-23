using CloudEngAgent.Mcp.Server.Sql;
using FluentAssertions;
using ModelContextProtocol;
using Xunit;

namespace CloudEngAgent.Mcp.Server.Tests;

public sealed class SqlIdentifierTests
{
    [Theory]
    [InlineData("dbo")]
    [InlineData("Customers")]
    [InlineData("_underscoreLeading")]
    [InlineData("Table_1")]
    [InlineData("a")]
    [InlineData("A1_b2_C3")]
    public void IsValid_AcceptsLegalIdentifiers(string identifier)
    {
        SqlIdentifier.IsValid(identifier).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(" leadingSpace")]
    [InlineData("trailingSpace ")]
    [InlineData("with space")]
    [InlineData("1startsWithDigit")]
    [InlineData("has-dash")]
    [InlineData("has.dot")]
    [InlineData("has;semi")]
    [InlineData("has'quote")]
    [InlineData("has\"quote")]
    [InlineData("has[bracket")]
    [InlineData("has]bracket")]
    [InlineData("Customers; DROP TABLE Users--")]
    [InlineData("Customers]; DELETE FROM x; --")]
    [InlineData("café")]
    public void IsValid_RejectsUnsafeOrEmpty(string? identifier)
    {
        SqlIdentifier.IsValid(identifier).Should().BeFalse();
    }

    [Fact]
    public void IsValid_RejectsTooLong()
    {
        var s = new string('a', 129);
        SqlIdentifier.IsValid(s).Should().BeFalse();
    }

    [Fact]
    public void IsValid_AcceptsMaxLength()
    {
        var s = new string('a', 128);
        SqlIdentifier.IsValid(s).Should().BeTrue();
    }

    [Fact]
    public void Quote_WrapsInBrackets()
    {
        SqlIdentifier.Quote("dbo").Should().Be("[dbo]");
        SqlIdentifier.Quote("Customers").Should().Be("[Customers]");
    }

    [Fact]
    public void Quote_ThrowsMcpExceptionForInvalid()
    {
        var act = () => SqlIdentifier.Quote("Customers; DROP TABLE Users--");
        act.Should().Throw<McpException>()
            .Which.ErrorCode.Should().Be(McpErrorCode.InvalidParams);
    }

    [Fact]
    public void Quote_ThrowsMcpExceptionForNull()
    {
        var act = () => SqlIdentifier.Quote(null);
        act.Should().Throw<McpException>();
    }
}
