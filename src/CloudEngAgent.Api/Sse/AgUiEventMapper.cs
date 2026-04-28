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
            RunEventType.UiWidgetSnapshot => MapWidgetSnapshot(evt),
            RunEventType.UiWidgetDelta => MapWidgetDelta(evt),
            RunEventType.UiInputRequested => MapInputRequested(evt),
            RunEventType.UiInputReceived => MapInputReceived(evt),
            RunEventType.ArtifactCreated => MapArtifactCreated(evt),
            _ => Array.Empty<(string, string)>(),
        };
    }

    private static (string, string)[] MapWidgetSnapshot(RunEvent evt)
    {
        try
        {
            using var doc = JsonDocument.Parse(evt.PayloadJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("widgetKey", out var keyEl) || keyEl.ValueKind != JsonValueKind.String)
            {
                return Array.Empty<(string, string)>();
            }

            if (!root.TryGetProperty("widget", out var widgetEl) || widgetEl.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<(string, string)>();
            }

            var widgetKey = keyEl.GetString()!;

            // Build {"state":{"widgets":{<widgetKey>: <widget>}}} preserving the inner widget element verbatim.
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("state");
                writer.WriteStartObject();
                writer.WritePropertyName("widgets");
                writer.WriteStartObject();
                writer.WritePropertyName(widgetKey);
                widgetEl.WriteTo(writer);
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            var json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            return new[] { ("StateSnapshot", json) };
        }
        catch (JsonException)
        {
            return Array.Empty<(string, string)>();
        }
    }

    private static (string, string)[] MapWidgetDelta(RunEvent evt)
    {
        try
        {
            using var doc = JsonDocument.Parse(evt.PayloadJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("widgetKey", out var keyEl) || keyEl.ValueKind != JsonValueKind.String)
            {
                return Array.Empty<(string, string)>();
            }

            if (!root.TryGetProperty("newRevision", out var revEl) || revEl.ValueKind != JsonValueKind.Number)
            {
                return Array.Empty<(string, string)>();
            }

            if (!root.TryGetProperty("patches", out var patchesEl) || patchesEl.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<(string, string)>();
            }

            var widgetKey = keyEl.GetString()!;
            var escapedKey = EscapeJsonPointer(widgetKey);
            var basePath = $"/widgets/{escapedKey}";

            string? newStatus = null;
            if (root.TryGetProperty("newStatus", out var statusEl) && statusEl.ValueKind == JsonValueKind.String)
            {
                newStatus = statusEl.GetString();
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("patches");
                writer.WriteStartArray();

                // Revision bump
                writer.WriteStartObject();
                writer.WriteString("op", "replace");
                writer.WriteString("path", $"{basePath}/revision");
                writer.WritePropertyName("value");
                revEl.WriteTo(writer);
                writer.WriteEndObject();

                // Status update if provided
                if (newStatus is not null)
                {
                    writer.WriteStartObject();
                    writer.WriteString("op", "replace");
                    writer.WriteString("path", $"{basePath}/status");
                    writer.WriteString("value", newStatus);
                    writer.WriteEndObject();
                }

                // Original patches re-rooted under /widgets/<key>/props
                foreach (var patch in patchesEl.EnumerateArray())
                {
                    if (patch.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    writer.WriteStartObject();
                    foreach (var prop in patch.EnumerateObject())
                    {
                        if (prop.NameEquals("path") && prop.Value.ValueKind == JsonValueKind.String)
                        {
                            var originalPath = prop.Value.GetString() ?? string.Empty;
                            writer.WriteString("path", $"{basePath}/props{originalPath}");
                        }
                        else
                        {
                            writer.WritePropertyName(prop.Name);
                            prop.Value.WriteTo(writer);
                        }
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            var json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            return new[] { ("StateDelta", json) };
        }
        catch (JsonException)
        {
            return Array.Empty<(string, string)>();
        }
    }

    private static (string, string)[] MapInputRequested(RunEvent evt)
    {
        try
        {
            using var doc = JsonDocument.Parse(evt.PayloadJson);
            var root = doc.RootElement;

            var value = new
            {
                inputRequestId = root.TryGetProperty("inputRequestId", out var id) ? id.GetString() : null,
                schemaRef = root.TryGetProperty("schemaRef", out var s) ? s.GetString() : null,
                expiresAt = root.TryGetProperty("expiresAt", out var e) ? e.GetString() : null,
                widgetKey = root.TryGetProperty("widgetKey", out var w) ? w.GetString() : null,
            };

            return new[]
            {
                ("Custom", JsonSerializer.Serialize(new { name = "input.requested", value })),
            };
        }
        catch (JsonException)
        {
            return Array.Empty<(string, string)>();
        }
    }

    private static (string, string)[] MapInputReceived(RunEvent evt)
    {
        try
        {
            using var doc = JsonDocument.Parse(evt.PayloadJson);
            var root = doc.RootElement;

            var value = new
            {
                inputRequestId = root.TryGetProperty("inputRequestId", out var id) ? id.GetString() : null,
                decision = root.TryGetProperty("decision", out var d) ? d.GetString() : null,
                receivedAt = root.TryGetProperty("receivedAt", out var r) ? r.GetString() : null,
            };

            return new[]
            {
                ("Custom", JsonSerializer.Serialize(new { name = "input.received", value })),
            };
        }
        catch (JsonException)
        {
            return Array.Empty<(string, string)>();
        }
    }

    private static (string, string)[] MapArtifactCreated(RunEvent evt)
    {
        try
        {
            using var doc = JsonDocument.Parse(evt.PayloadJson);
            var root = doc.RootElement;

            var value = new
            {
                artifactId = root.TryGetProperty("artifactId", out var id) ? id.GetString() : null,
                kind = root.TryGetProperty("kind", out var k) ? k.GetString() : null,
                filename = root.TryGetProperty("filename", out var f) ? f.GetString() : null,
                contentType = root.TryGetProperty("contentType", out var c) ? c.GetString() : null,
                sizeBytes = root.TryGetProperty("sizeBytes", out var sb) && sb.ValueKind == JsonValueKind.Number
                    ? sb.GetInt64()
                    : (long?)null,
            };

            return new[]
            {
                ("Custom", JsonSerializer.Serialize(new { name = "artifact.created", value })),
            };
        }
        catch (JsonException)
        {
            return Array.Empty<(string, string)>();
        }
    }

    private static string EscapeJsonPointer(string s)
    {
        // RFC 6901 §4: escape '~' first to '~0', then '/' to '~1'.
        return s.Replace("~", "~0", StringComparison.Ordinal)
                .Replace("/", "~1", StringComparison.Ordinal);
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
