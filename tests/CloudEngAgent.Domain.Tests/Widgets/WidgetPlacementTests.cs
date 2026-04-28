using CloudEngAgent.Domain.Widgets;
using FluentAssertions;
using Xunit;

namespace CloudEngAgent.Domain.Tests.Widgets;

public class WidgetPlacementTests
{
    [Theory]
    [InlineData("chat")]
    [InlineData("side-panel")]
    [InlineData("timeline")]
    [InlineData("artifact-panel")]
    public void Constructor_accepts_known_surfaces(string surface)
    {
        var placement = new WidgetPlacement(surface, "step-1", "agent-1", null);

        placement.Surface.Should().Be(surface);
        placement.StepId.Should().Be("step-1");
        placement.AgentId.Should().Be("agent-1");
        placement.ParentMessageId.Should().BeNull();
    }

    [Fact]
    public void Constructor_throws_for_unknown_surface()
    {
        var act = () => new WidgetPlacement("modal", "s", "a", null);

        act.Should().Throw<ArgumentException>().WithMessage("*Unknown surface*");
    }

    [Theory]
    [InlineData("", "step", "agent")]
    [InlineData("chat", "", "agent")]
    [InlineData("chat", "step", "")]
    [InlineData("   ", "step", "agent")]
    public void Constructor_throws_for_empty_required_fields(string surface, string stepId, string agentId)
    {
        var act = () => new WidgetPlacement(surface, stepId, agentId, null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ParentMessageId_is_optional()
    {
        var placement = new WidgetPlacement("chat", "s", "a", "msg-42");

        placement.ParentMessageId.Should().Be("msg-42");
    }
}
