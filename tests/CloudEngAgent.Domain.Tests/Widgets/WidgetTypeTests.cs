using CloudEngAgent.Domain.Widgets;
using FluentAssertions;
using Xunit;

namespace CloudEngAgent.Domain.Tests.Widgets;

public class WidgetTypeTests
{
    [Theory]
    [InlineData("result-table")]
    [InlineData("bar-chart")]
    [InlineData("kpi-cards")]
    [InlineData("findings-list")]
    [InlineData("ddl-diff")]
    [InlineData("approval-card")]
    [InlineData("file-download")]
    [InlineData("markdown-report")]
    public void Parse_returns_instance_for_known_id(string value)
    {
        var type = WidgetType.Parse(value);

        type.Value.Should().Be(value);
        type.ToString().Should().Be(value);
    }

    [Fact]
    public void Parse_throws_for_unknown_id()
    {
        var act = () => WidgetType.Parse("er-diagram");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Unknown widget type*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_throws_for_empty(string value)
    {
        var act = () => WidgetType.Parse(value);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void All_contains_eight_widget_types()
    {
        WidgetType.All.Should().HaveCount(8);
    }

    [Fact]
    public void Static_instances_are_equal_to_parsed_values()
    {
        WidgetType.Parse("approval-card").Should().Be(WidgetType.ApprovalCard);
        WidgetType.Parse("markdown-report").Should().Be(WidgetType.MarkdownReport);
    }

    [Fact]
    public void Equality_is_value_based()
    {
        var a = new WidgetType("result-table");
        var b = new WidgetType("result-table");

        (a == b).Should().BeTrue();
        a.Should().Be(WidgetType.ResultTable);
    }
}
