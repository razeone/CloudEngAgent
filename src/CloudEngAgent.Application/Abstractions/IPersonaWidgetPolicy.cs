using CloudEngAgent.Domain.Widgets;

namespace CloudEngAgent.Application.Abstractions;

/// <summary>
/// Optional per-persona widget allow-list, sourced from persona YAML's
/// (optional) <c>allowedWidgets</c> field. Kept separate from the
/// <see cref="CloudEngAgent.Domain.Personas.AgentPersona"/> domain record so
/// that adding the allow-list does not cascade through every persona call site.
/// </summary>
public interface IPersonaWidgetPolicy
{
    /// <summary>
    /// Returns the per-persona allowed widget types, or <c>null</c> if the
    /// persona has no restriction (any widget type is allowed). Returns an
    /// empty list to deny all widgets for that persona.
    /// </summary>
    IReadOnlyList<WidgetType>? GetAllowedWidgets(string personaId);
}

/// <summary>
/// Default policy used when no real persona policy is wired up: all personas
/// are allowed to emit any widget type. Used by tests and as a fallback in DI.
/// </summary>
public sealed class NullPersonaWidgetPolicy : IPersonaWidgetPolicy
{
    public static NullPersonaWidgetPolicy Instance { get; } = new();

    public IReadOnlyList<WidgetType>? GetAllowedWidgets(string personaId) => null;
}
