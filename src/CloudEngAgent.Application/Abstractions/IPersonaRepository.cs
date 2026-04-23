using CloudEngAgent.Domain.Personas;

namespace CloudEngAgent.Application.Abstractions;

public interface IPersonaRepository
{
    Task<AgentPersona?> GetAsync(string id, CancellationToken cancellationToken);

    IAsyncEnumerable<AgentPersona> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Raised when the underlying persona source detects an add, update, or removal.
    /// Implementations that cannot detect changes (e.g., in-memory) never raise this event.
    /// </summary>
    event EventHandler<PersonaChangedEventArgs>? PersonaChanged;
}

public enum PersonaChangeKind
{
    Added,
    Updated,
    Removed,
}

public sealed class PersonaChangedEventArgs(string personaId, PersonaChangeKind kind) : EventArgs
{
    public string PersonaId { get; } = personaId;
    public PersonaChangeKind Kind { get; } = kind;
}
