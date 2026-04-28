using CloudEngAgent.Domain.Runs;
using FluentAssertions;
using Xunit;

namespace CloudEngAgent.Domain.Tests;

public class RunEventTypeNumericStabilityTests
{
    [Theory]
    [InlineData(RunEventType.RunStarted, 0)]
    [InlineData(RunEventType.TextDelta, 1)]
    [InlineData(RunEventType.ToolCall, 2)]
    [InlineData(RunEventType.ToolResult, 3)]
    [InlineData(RunEventType.AgentHandoff, 4)]
    [InlineData(RunEventType.RunFinished, 5)]
    [InlineData(RunEventType.Error, 6)]
    [InlineData(RunEventType.UiWidgetSnapshot, 7)]
    [InlineData(RunEventType.UiWidgetDelta, 8)]
    [InlineData(RunEventType.UiInputRequested, 9)]
    [InlineData(RunEventType.UiInputReceived, 10)]
    [InlineData(RunEventType.ArtifactCreated, 11)]
    public void RunEventType_values_have_stable_numeric_assignments(RunEventType type, int expected)
    {
        ((int)type).Should().Be(expected);
    }

    [Fact]
    public void RunStatus_PendingInput_exists()
    {
        Enum.IsDefined(typeof(RunStatus), RunStatus.PendingInput).Should().BeTrue();
    }
}
