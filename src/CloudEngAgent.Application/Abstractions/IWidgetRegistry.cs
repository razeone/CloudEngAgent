using CloudEngAgent.Domain.Widgets;

namespace CloudEngAgent.Application.Abstractions;

/// <summary>
/// Server-side widget registry: holds JSON Schemas, per-type policies, and the
/// per-persona allow-list. Every <see cref="IUiSurface"/> emit goes through
/// <see cref="Validate"/> so that malformed widget payloads never reach the
/// AG-UI stream.
/// </summary>
public interface IWidgetRegistry
{
    /// <summary>Returns the JSON Schema for the given widget type, as a string.</summary>
    string GetSchemaJson(WidgetType type);

    /// <summary>Validates a props payload (already serialized to JSON) against the type's schema.</summary>
    WidgetValidationResult Validate(WidgetType type, string propsJson);

    /// <summary>Returns the per-type policy: max payload bytes, max rows, allowed personas, etc.</summary>
    WidgetPolicy GetPolicy(WidgetType type);

    /// <summary>True if the persona is allowed to emit this widget type (intersection with persona YAML allowedWidgets).</summary>
    bool IsAllowed(string personaId, WidgetType type);

    /// <summary>All registered widget types (for the GET /v1/widgets/registry endpoint).</summary>
    IReadOnlyList<WidgetType> Types { get; }
}

public sealed record WidgetValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public sealed record WidgetPolicy(
    int MaxPayloadBytes,
    int? MaxRows,
    int? MaxSeries,
    bool CanRequestInput,
    bool CanAttachArtifacts,
    IReadOnlyList<string> AllowedPersonas);   // empty = any persona allowed
