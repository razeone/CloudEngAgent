using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Domain.Widgets;
using CloudEngAgent.Infrastructure.Widgets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CloudEngAgent.Infrastructure.Tests.Widgets;

public sealed class UiSurfaceTests
{
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-000000000abc");

    private static UiSurface CreateSurface(string personaId = "performance",
        IPersonaWidgetPolicy? personaPolicy = null)
    {
        var registry = new WidgetRegistry(
            personaPolicy ?? NullPersonaWidgetPolicy.Instance,
            NullLogger<WidgetRegistry>.Instance);
        return new UiSurface(registry, personaId, NullLogger<UiSurface>.Instance);
    }

    private static WidgetKey Key(string local = "kpi") =>
        new(RunId, "step-1", "performance", local);

    private static WidgetPlacement Placement() =>
        new("chat", "step-1", "performance", null);

    [Fact]
    public async Task EmitSnapshotAsync_returns_state_for_valid_payload()
    {
        var surface = CreateSurface();
        var props = new { cards = new[] { new { label = "CPU", value = 42 } } };

        var state = await surface.EmitSnapshotAsync(
            Key(), WidgetType.KpiCards, WidgetStatus.Complete, Placement(), props);

        state.Type.Should().Be(WidgetType.KpiCards);
        state.Revision.Should().Be(1);
        state.PropsJson.Should().Contain("\"label\":\"CPU\"");
    }

    [Fact]
    public async Task EmitSnapshotAsync_rejects_disallowed_widget_for_persona()
    {
        var policy = new SinglePersonaPolicy("analyst", new[] { WidgetType.ResultTable });
        var surface = CreateSurface(personaId: "analyst", personaPolicy: policy);
        var props = new { cards = new[] { new { label = "CPU", value = 42 } } };

        var act = () => surface.EmitSnapshotAsync(
            Key(), WidgetType.KpiCards, WidgetStatus.Complete, Placement(), props);

        (await act.Should().ThrowAsync<WidgetEmitException>())
            .WithMessage("*not allowed to emit widget type 'kpi-cards'*");
    }

    [Fact]
    public async Task EmitSnapshotAsync_rejects_oversized_payload()
    {
        var surface = CreateSurface();
        // file-download cap is 8_192 bytes; build a filename that pushes us over it.
        var props = new
        {
            artifactId = "22222222-2222-2222-2222-222222222222",
            filename = new string('a', 16_000),
            contentType = "application/pdf",
            sizeBytes = 1024,
        };

        var act = () => surface.EmitSnapshotAsync(
            Key("dl"), WidgetType.FileDownload, WidgetStatus.Complete, Placement(), props);

        (await act.Should().ThrowAsync<WidgetEmitException>())
            .WithMessage("*exceeds policy max 8192 bytes*");
    }

    [Fact]
    public async Task EmitSnapshotAsync_rejects_schema_invalid_props()
    {
        var surface = CreateSurface();
        // bar-chart requires 'series'; missing it must fail validation.
        var props = new { title = "t", xLabel = "x", yLabel = "y" };

        var act = () => surface.EmitSnapshotAsync(
            Key("chart"), WidgetType.BarChart, WidgetStatus.Complete, Placement(), props);

        (await act.Should().ThrowAsync<WidgetEmitException>())
            .WithMessage("*failed schema validation*");
    }

    [Fact]
    public async Task PatchAsync_throws_NotSupported()
    {
        var surface = CreateSurface();
        var act = () => surface.PatchAsync(Key(), Array.Empty<JsonPatchOp>());
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    private sealed class SinglePersonaPolicy(string personaId, IReadOnlyList<WidgetType> allowed)
        : IPersonaWidgetPolicy
    {
        public IReadOnlyList<WidgetType>? GetAllowedWidgets(string id) =>
            string.Equals(id, personaId, StringComparison.Ordinal) ? allowed : null;
    }
}
