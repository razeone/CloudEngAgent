using CloudEngAgent.Domain.Backends;
using CloudEngAgent.Domain.Tools;

namespace CloudEngAgent.Domain.Personas;

public sealed record AgentPersona(
    string Id,
    string Name,
    string SystemPrompt,
    BackendId Backend,
    IReadOnlyList<ToolRef> Tools,
    Guardrails Guardrails,
    PersonaVersion Version);
