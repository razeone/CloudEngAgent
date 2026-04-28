using CloudEngAgent.Domain.Widgets;
using FluentAssertions;
using Xunit;

namespace CloudEngAgent.Domain.Tests.Widgets;

public class WidgetStateTests
{
    private static WidgetKey NewKey() =>
        new(Guid.NewGuid(), "step-1", "agent-1", "local-1");

    private static WidgetPlacement NewPlacement() =>
        new("chat", "step-1", "agent-1", null);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Constructor_throws_for_non_positive_revision(int revision)
    {
        var act = () => new WidgetState(
            NewKey(),
            WidgetType.ResultTable,
            WidgetStatus.Loading,
            revision,
            NewPlacement(),
            "{}",
            Array.Empty<ArtifactRef>());

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_accepts_revision_one_and_empty_artifacts()
    {
        var state = new WidgetState(
            NewKey(),
            WidgetType.KpiCards,
            WidgetStatus.Loading,
            1,
            NewPlacement(),
            "{}",
            Array.Empty<ArtifactRef>());

        state.Revision.Should().Be(1);
        state.Artifacts.Should().BeEmpty();
        state.Type.Should().Be(WidgetType.KpiCards);
    }
}
