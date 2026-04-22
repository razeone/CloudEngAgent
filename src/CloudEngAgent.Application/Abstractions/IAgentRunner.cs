using CloudEngAgent.Domain.Personas;
using CloudEngAgent.Domain.Runs;

namespace CloudEngAgent.Application.Abstractions;

public interface IAgentRunner
{
    IAsyncEnumerable<RunEvent> RunAsync(
        AgentPersona persona,
        IReadOnlyList<Message> history,
        Guid runId,
        CancellationToken cancellationToken);
}
