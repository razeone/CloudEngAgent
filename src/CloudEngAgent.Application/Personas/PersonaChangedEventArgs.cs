using CloudEngAgent.Domain.Personas;

namespace CloudEngAgent.Application.Personas;

public sealed class PersonaChangedEventArgs(AgentPersona persona, PersonaChangeKind kind) : EventArgs
{
    public AgentPersona Persona { get; } = persona;

    public PersonaChangeKind Kind { get; } = kind;
}

public enum PersonaChangeKind
{
    Added,
    Updated,
    Removed,
}
