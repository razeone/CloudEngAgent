using System.Text.Json;
using CloudEngAgent.Api.Sse;
using CloudEngAgent.Domain.Runs;
using FluentAssertions;
using Xunit;

namespace CloudEngAgent.Api.Tests.Sse;

public class AgUiEventMapperTests
{
    private static readonly Guid RunId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string ThreadId = "thread-1";

    private static RunEvent Make(RunEventType type, string payload, long seq = 1) =>
        new(RunId, type, payload, seq, DateTimeOffset.UtcNow);

    // ---------------- Existing-event regression smoke tests ----------------

    [Fact]
    public void Maps_RunStarted_to_AgUi_RunStarted()
    {
        var evt = Make(RunEventType.RunStarted, "{}");
        var frames = AgUiEventMapper.Map(evt, RunId, ThreadId);

        frames.Should().HaveCount(1);
        frames[0].EventType.Should().Be("RunStarted");
        using var doc = JsonDocument.Parse(frames[0].DataJson);
        doc.RootElement.GetProperty("runId").GetGuid().Should().Be(RunId);
        doc.RootElement.GetProperty("threadId").GetString().Should().Be(ThreadId);
    }

    [Fact]
    public void Maps_TextDelta()
    {
        var payload = JsonSerializer.Serialize(new { messageId = "m1", role = "assistant", delta = "hello" });
        var evt = Make(RunEventType.TextDelta, payload);
        var frames = AgUiEventMapper.Map(evt, RunId, ThreadId);

        frames.Should().HaveCount(1);
        frames[0].EventType.Should().Be("TextMessageChunk");
    }

    [Fact]
    public void Maps_ToolCall_to_three_frames()
    {
        var payload = JsonSerializer.Serialize(new { toolCallId = "c1", toolName = "x", arguments = "{}" });
        var evt = Make(RunEventType.ToolCall, payload);
        var frames = AgUiEventMapper.Map(evt, RunId, ThreadId);

        frames.Select(f => f.EventType).Should().Equal("ToolCallStart", "ToolCallArgs", "ToolCallEnd");
    }

    // ---------------- UiWidgetSnapshot ----------------

    [Fact]
    public void UiWidgetSnapshot_emits_StateSnapshot_with_widget_state()
    {
        const string widgetKey = "run:abc/step:performance/agent:performance/widget:slow-queries";
        var widget = new
        {
            type = "result-table",
            status = "streaming",
            revision = 7,
            placement = new { surface = "timeline", stepId = "performance", agentId = "performance", parentMessageId = (string?)null },
            props = new { columns = new[] { "schema", "table" }, rows = Array.Empty<object>() },
            artifacts = new[] { new { artifactId = "a1", filename = "f.csv", contentType = "text/csv", sizeBytes = 1234 } },
        };
        var payload = JsonSerializer.Serialize(new { widgetKey, widget });

        var frames = AgUiEventMapper.Map(Make(RunEventType.UiWidgetSnapshot, payload), RunId, ThreadId);

        frames.Should().HaveCount(1);
        frames[0].EventType.Should().Be("StateSnapshot");

        using var doc = JsonDocument.Parse(frames[0].DataJson);
        var widgetEl = doc.RootElement.GetProperty("state").GetProperty("widgets").GetProperty(widgetKey);
        widgetEl.GetProperty("type").GetString().Should().Be("result-table");
        widgetEl.GetProperty("status").GetString().Should().Be("streaming");
        widgetEl.GetProperty("revision").GetInt32().Should().Be(7);
        widgetEl.GetProperty("placement").GetProperty("surface").GetString().Should().Be("timeline");
        widgetEl.GetProperty("props").GetProperty("columns").GetArrayLength().Should().Be(2);
        widgetEl.GetProperty("artifacts").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void UiWidgetSnapshot_with_empty_artifacts_still_emits_frame()
    {
        const string widgetKey = "run:abc/widget:x";
        var widget = new
        {
            type = "kpi-cards",
            status = "complete",
            revision = 1,
            placement = new { surface = "timeline", stepId = "s", agentId = "a", parentMessageId = (string?)null },
            props = new { cards = Array.Empty<object>() },
            artifacts = Array.Empty<object>(),
        };
        var payload = JsonSerializer.Serialize(new { widgetKey, widget });

        var frames = AgUiEventMapper.Map(Make(RunEventType.UiWidgetSnapshot, payload), RunId, ThreadId);

        frames.Should().HaveCount(1);
        using var doc = JsonDocument.Parse(frames[0].DataJson);
        var widgetEl = doc.RootElement.GetProperty("state").GetProperty("widgets").GetProperty(widgetKey);
        widgetEl.GetProperty("artifacts").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void UiWidgetSnapshot_malformed_returns_empty()
    {
        var frames = AgUiEventMapper.Map(Make(RunEventType.UiWidgetSnapshot, "not-json"), RunId, ThreadId);
        frames.Should().BeEmpty();
    }

    [Fact]
    public void UiWidgetSnapshot_missing_widget_returns_empty()
    {
        var payload = JsonSerializer.Serialize(new { widgetKey = "x" });
        var frames = AgUiEventMapper.Map(Make(RunEventType.UiWidgetSnapshot, payload), RunId, ThreadId);
        frames.Should().BeEmpty();
    }

    // ---------------- UiWidgetDelta ----------------

    [Fact]
    public void UiWidgetDelta_with_no_status_emits_revision_plus_rerooted_patches()
    {
        const string widgetKey = "run:abc/step:perf/agent:p/widget:x";
        var payload = JsonSerializer.Serialize(new
        {
            widgetKey,
            baseRevision = 6,
            newRevision = 7,
            newStatus = (string?)null,
            patches = new[]
            {
                new { op = "add", path = "/rows/-", value = new { schema = "dbo", table = "Posts" } },
            },
        });

        var frames = AgUiEventMapper.Map(Make(RunEventType.UiWidgetDelta, payload), RunId, ThreadId);

        frames.Should().HaveCount(1);
        frames[0].EventType.Should().Be("StateDelta");

        using var doc = JsonDocument.Parse(frames[0].DataJson);
        var patches = doc.RootElement.GetProperty("patches");
        patches.GetArrayLength().Should().Be(2);

        var escapedKey = "run:abc~1step:perf~1agent:p~1widget:x";
        patches[0].GetProperty("op").GetString().Should().Be("replace");
        patches[0].GetProperty("path").GetString().Should().Be($"/widgets/{escapedKey}/revision");
        patches[0].GetProperty("value").GetInt32().Should().Be(7);

        patches[1].GetProperty("op").GetString().Should().Be("add");
        patches[1].GetProperty("path").GetString().Should().Be($"/widgets/{escapedKey}/props/rows/-");
        patches[1].GetProperty("value").GetProperty("schema").GetString().Should().Be("dbo");
    }

    [Fact]
    public void UiWidgetDelta_with_status_emits_three_patches()
    {
        const string widgetKey = "run:abc/widget:x";
        var payload = JsonSerializer.Serialize(new
        {
            widgetKey,
            baseRevision = 6,
            newRevision = 7,
            newStatus = "complete",
            patches = new[]
            {
                new { op = "replace", path = "/title", value = "done" },
            },
        });

        var frames = AgUiEventMapper.Map(Make(RunEventType.UiWidgetDelta, payload), RunId, ThreadId);

        frames.Should().HaveCount(1);
        using var doc = JsonDocument.Parse(frames[0].DataJson);
        var patches = doc.RootElement.GetProperty("patches");
        patches.GetArrayLength().Should().Be(3);

        patches[0].GetProperty("path").GetString().Should().EndWith("/revision");
        patches[1].GetProperty("path").GetString().Should().EndWith("/status");
        patches[1].GetProperty("value").GetString().Should().Be("complete");
        patches[2].GetProperty("path").GetString().Should().EndWith("/props/title");
        patches[2].GetProperty("value").GetString().Should().Be("done");
    }

    [Fact]
    public void UiWidgetDelta_escapes_widgetKey_per_RFC6901()
    {
        const string widgetKey = "a~b/c";
        var payload = JsonSerializer.Serialize(new
        {
            widgetKey,
            baseRevision = 0,
            newRevision = 1,
            patches = new[] { new { op = "replace", path = "/x", value = 1 } },
        });

        var frames = AgUiEventMapper.Map(Make(RunEventType.UiWidgetDelta, payload), RunId, ThreadId);

        using var doc = JsonDocument.Parse(frames[0].DataJson);
        var patches = doc.RootElement.GetProperty("patches");
        patches[0].GetProperty("path").GetString().Should().Be("/widgets/a~0b~1c/revision");
        patches[1].GetProperty("path").GetString().Should().Be("/widgets/a~0b~1c/props/x");
    }

    [Fact]
    public void UiWidgetDelta_reroots_multiple_patches()
    {
        const string widgetKey = "k";
        var payload = JsonSerializer.Serialize(new
        {
            widgetKey,
            baseRevision = 0,
            newRevision = 2,
            patches = new object[]
            {
                new { op = "add", path = "/rows/-", value = (object)1 },
                new { op = "replace", path = "/totalKnown", value = (object)true },
                new { op = "remove", path = "/error" },
            },
        });

        var frames = AgUiEventMapper.Map(Make(RunEventType.UiWidgetDelta, payload), RunId, ThreadId);

        using var doc = JsonDocument.Parse(frames[0].DataJson);
        var patches = doc.RootElement.GetProperty("patches");
        patches.GetArrayLength().Should().Be(4);
        patches[1].GetProperty("path").GetString().Should().Be("/widgets/k/props/rows/-");
        patches[2].GetProperty("path").GetString().Should().Be("/widgets/k/props/totalKnown");
        patches[3].GetProperty("path").GetString().Should().Be("/widgets/k/props/error");
    }

    [Fact]
    public void UiWidgetDelta_malformed_returns_empty()
    {
        var frames = AgUiEventMapper.Map(Make(RunEventType.UiWidgetDelta, "{not json"), RunId, ThreadId);
        frames.Should().BeEmpty();
    }

    // ---------------- UiInputRequested ----------------

    [Fact]
    public void UiInputRequested_emits_Custom_input_requested()
    {
        var payload = JsonSerializer.Serialize(new
        {
            inputRequestId = "req-1",
            runId = RunId.ToString(),
            stepId = "performance",
            agentId = "performance",
            schemaRef = "approval-card@v1",
            expiresAt = "2026-01-01T00:00:00Z",
            widgetKey = "run:abc/widget:approval-1",
        });

        var frames = AgUiEventMapper.Map(Make(RunEventType.UiInputRequested, payload), RunId, ThreadId);

        frames.Should().HaveCount(1);
        frames[0].EventType.Should().Be("Custom");

        using var doc = JsonDocument.Parse(frames[0].DataJson);
        doc.RootElement.GetProperty("name").GetString().Should().Be("input.requested");
        var v = doc.RootElement.GetProperty("value");
        v.GetProperty("inputRequestId").GetString().Should().Be("req-1");
        v.GetProperty("schemaRef").GetString().Should().Be("approval-card@v1");
        v.GetProperty("expiresAt").GetString().Should().Be("2026-01-01T00:00:00Z");
        v.GetProperty("widgetKey").GetString().Should().Be("run:abc/widget:approval-1");
    }

    // ---------------- UiInputReceived ----------------

    [Theory]
    [InlineData("accept")]
    [InlineData("reject")]
    public void UiInputReceived_emits_Custom_input_received(string decision)
    {
        var payload = JsonSerializer.Serialize(new
        {
            inputRequestId = "req-1",
            decision,
            receivedAt = "2026-01-01T00:00:01Z",
        });

        var frames = AgUiEventMapper.Map(Make(RunEventType.UiInputReceived, payload), RunId, ThreadId);

        frames.Should().HaveCount(1);
        frames[0].EventType.Should().Be("Custom");

        using var doc = JsonDocument.Parse(frames[0].DataJson);
        doc.RootElement.GetProperty("name").GetString().Should().Be("input.received");
        var v = doc.RootElement.GetProperty("value");
        v.GetProperty("inputRequestId").GetString().Should().Be("req-1");
        v.GetProperty("decision").GetString().Should().Be(decision);
        v.GetProperty("receivedAt").GetString().Should().Be("2026-01-01T00:00:01Z");
    }

    // ---------------- ArtifactCreated ----------------

    [Fact]
    public void ArtifactCreated_emits_Custom_artifact_created_without_runId()
    {
        var payload = JsonSerializer.Serialize(new
        {
            artifactId = "a-1",
            runId = RunId.ToString(),
            kind = "Pdf",
            filename = "report.pdf",
            contentType = "application/pdf",
            sizeBytes = 4096L,
        });

        var frames = AgUiEventMapper.Map(Make(RunEventType.ArtifactCreated, payload), RunId, ThreadId);

        frames.Should().HaveCount(1);
        frames[0].EventType.Should().Be("Custom");

        using var doc = JsonDocument.Parse(frames[0].DataJson);
        doc.RootElement.GetProperty("name").GetString().Should().Be("artifact.created");
        var v = doc.RootElement.GetProperty("value");
        v.GetProperty("artifactId").GetString().Should().Be("a-1");
        v.GetProperty("kind").GetString().Should().Be("Pdf");
        v.GetProperty("filename").GetString().Should().Be("report.pdf");
        v.GetProperty("contentType").GetString().Should().Be("application/pdf");
        v.GetProperty("sizeBytes").GetInt64().Should().Be(4096L);
        v.TryGetProperty("runId", out _).Should().BeFalse();
    }

    [Fact]
    public void ArtifactCreated_malformed_returns_empty()
    {
        var frames = AgUiEventMapper.Map(Make(RunEventType.ArtifactCreated, "garbage"), RunId, ThreadId);
        frames.Should().BeEmpty();
    }
}
