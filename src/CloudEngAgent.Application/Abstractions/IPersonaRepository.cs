using CloudEngAgent.Application.Personas;
using CloudEngAgent.Domain.Personas;

namespace CloudEngAgent.Application.Abstractions;

public interface IPersonaRepository
{
    Task<AgentPersona?> GetAsync(string id, CancellationToken cancellationToken);

    IAsyncEnumerable<AgentPersona> ListAsync(CancellationToken cancellationToken);

    event EventHandler<PersonaChangedEventArgs>? PersonaChanged;
}
