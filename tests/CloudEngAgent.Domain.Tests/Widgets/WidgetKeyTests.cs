using CloudEngAgent.Domain.Widgets;
using FluentAssertions;
using Xunit;

namespace CloudEngAgent.Domain.Tests.Widgets;

public class WidgetKeyTests
{
    [Fact]
    public void ToCanonicalString_uses_namespaced_format()
    {
        var runId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var key = new WidgetKey(runId, "step-1", "performance", "kpi");

        key.ToCanonicalString().Should().Be(
            "run:11111111-2222-3333-4444-555555555555/step:step-1/agent:performance/widget:kpi");
    }

    [Fact]
    public void Parse_round_trips_with_ToCanonicalString()
    {
        var original = new WidgetKey(Guid.NewGuid(), "step-A", "analyst", "table-1");

        var parsed = WidgetKey.Parse(original.ToCanonicalString());

        parsed.Should().Be(original);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-key")]
    [InlineData("run:not-a-guid/step:s/agent:a/widget:w")]
    [InlineData("run:11111111-2222-3333-4444-555555555555/step:s/agent:a")]
    [InlineData("step:s/agent:a/widget:w")]
    public void Parse_throws_FormatException_for_bad_input(string canonical)
    {
        var act = () => WidgetKey.Parse(canonical);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Constructor_throws_for_empty_run_id()
    {
        var act = () => new WidgetKey(Guid.Empty, "step", "agent", "local");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("", "agent", "local")]
    [InlineData("step", "", "local")]
    [InlineData("step", "agent", "")]
    public void Constructor_throws_for_empty_segments(string step, string agent, string local)
    {
        var act = () => new WidgetKey(Guid.NewGuid(), step, agent, local);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Equality_is_value_based()
    {
        var runId = Guid.NewGuid();
        var a = new WidgetKey(runId, "s", "a", "l");
        var b = new WidgetKey(runId, "s", "a", "l");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }
}
