using System.Text.Json;

namespace CloudEngAgent.Application.Abstractions;

/// <summary>
/// A single RFC 6902 JSON Patch operation.
/// </summary>
public sealed record JsonPatchOp(string Op, string Path, JsonElement? Value, string? From);
