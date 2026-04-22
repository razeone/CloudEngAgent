using System.Text.Json;
using CloudEngAgent.Domain.Runs;

namespace CloudEngAgent.Api.Sse;

/// <summary>
/// Maps internal <see cref="RunEvent"/>s to AG-UI protocol events
/// (https://docs.ag-ui.com/concepts/events). Returned tuples are written to
/// the SSE response in order; each tuple becomes one <c>event:</c>/<c>data:</c>
/// frame on the wire.
/// </summary>
internal static class AgUiEventMapper
{
    public static IReadOnlyList<(string EventType, string DataJson)> Map(RunEvent evt, Guid runId, string? threadId)
    {
        return evt.Type switch
        {
            RunEventType.RunStarted => new[]
            {
                ("RunStarted", JsonSerializer.Serialize(new { threadId, runId })),
            },
            RunEventType.RunFinished => new[]
            {
                ("RunFinished", JsonSerializer.Serialize(new { threadId, runId, result = TryParse(evt.PayloadJson) })),
            },
            RunEventType.Error => new[]
            {
                ("RunError", evt.PayloadJson),
            },
            RunEventType.AgentHandoff => MapHandoff(evt),
            RunEventType.TextDelta => MapText(evt),
            RunEventType.ToolCall => MapToolCall(evt),
            RunEventType.ToolResult => MapToolResult(evt),
            _ => Array.Empty<(string, string)>(),
        };
    }

    private static (string, string)[] MapHandoff(RunEvent evt)
    {
        using var doc = JsonDocument.Parse(evt.PayloadJson);
        var from = doc.RootElement.TryGetProperty("from", out var f) ? f.GetString() : null;
        var to = doc.RootElement.TryGetProperty("to", out var t) ? t.GetString() : null;

        var frames = new List<(string, string)>(2);
        if (!string.IsNullOrEmpty(from))
        {
            frames.Add(("StepFinished", JsonSerializer.Serialize(new { stepName = from })));
        }

        if (!string.IsNullOrEmpty(to))
        {
            frames.Add(("StepStarted", JsonSerializer.Serialize(new { stepName = to })));
        }

        return frames.ToArray();
    }

    private static (string, string)[] MapText(RunEvent evt)
    {
        using var doc = JsonDocument.Parse(evt.PayloadJson);
        var root = doc.RootElement;
        var payload = new
        {
            messageId = root.TryGetProperty("messageId", out var m) ? m.GetString() : Guid.NewGuid().ToString("n"),
            role = root.TryGetProperty("role", out var r) ? r.GetString() : "assistant",
            delta = root.TryGetProperty("delta", out var d) ? d.GetString() : string.Empty,
        };
        return new[] { ("TextMessageChunk", JsonSerializer.Serialize(payload)) };
    }

    private static (string, string)[] MapToolCall(RunEvent evt)
    {
        using var doc = JsonDocument.Parse(evt.PayloadJson);
        var root = doc.RootElement;
        var toolCallId = root.TryGetProperty("toolCallId", out var id) ? id.GetString() ?? Guid.NewGuid().ToString("n") : Guid.NewGuid().ToString("n");
        var toolCallName = root.TryGetProperty("toolName", out var n) ? n.GetString() : "unknown";
        var arguments = root.TryGetProperty("arguments", out var a) ? a.GetString() ?? "{}" : "{}";

        return new[]
        {
            ("ToolCallStart", JsonSerializer.Serialize(new { toolCallId, toolCallName })),
            ("ToolCallArgs", JsonSerializer.Serialize(new { toolCallId, delta = arguments })),
            ("ToolCallEnd", JsonSerializer.Serialize(new { toolCallId })),
        };
    }

    private static (string, string)[] MapToolResult(RunEvent evt)
    {
        using var doc = JsonDocument.Parse(evt.PayloadJson);
        var root = doc.RootElement;
        var payload = new
        {
            toolCallId = root.TryGetProperty("toolCallId", out var id) ? id.GetString() : null,
            isError = root.TryGetProperty("isError", out var e) && e.ValueKind == JsonValueKind.True,
            content = root.TryGetProperty("content", out var c) ? c.GetString() : null,
        };
        return new[] { ("ToolCallResult", JsonSerializer.Serialize(payload)) };
    }

    private static object? TryParse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
