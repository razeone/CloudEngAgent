using CloudEngAgent.Domain.Backends;
using FluentAssertions;
using Xunit;

namespace CloudEngAgent.Domain.Tests;

public class BackendIdTests
{
    [Theory]
    [InlineData("azure-openai")]
    [InlineData("azure-foundry")]
    [InlineData("anthropic")]
    [InlineData("github-models")]
    [InlineData("openai")]
    public void Parse_returns_instance_for_known_id(string value)
    {
        var id = BackendId.Parse(value);

        id.Value.Should().Be(value);
        id.ToString().Should().Be(value);
    }

    [Fact]
    public void Parse_throws_for_unknown_id()
    {
        var act = () => BackendId.Parse("claude-copilot");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Unknown backend id*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParse_returns_false_for_empty_or_null(string? value)
    {
        var ok = BackendId.TryParse(value, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void All_contains_five_backends()
    {
        BackendId.All.Should().HaveCount(5);
    }

    [Fact]
    public void Parse_returns_same_reference_as_static_catalog()
    {
        BackendId.Parse("anthropic").Should().BeSameAs(BackendId.Anthropic);
    }
}
