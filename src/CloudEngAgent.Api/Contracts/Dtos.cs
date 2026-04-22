using CloudEngAgent.Domain.Personas;
using CloudEngAgent.Domain.Runs;
using CloudEngAgent.Domain.Workflows;

namespace CloudEngAgent.Api.Contracts;

public sealed record WorkflowDto(string Id, string Name, IReadOnlyList<string> PersonaIds, string EntryPersonaId)
{
    public static WorkflowDto FromDomain(WorkflowDefinition w) =>
        new(w.Id, w.Name, w.PersonaIds, w.EntryPersonaId);
}

public sealed record PersonaDto(string Id, string Name, string Backend, string Version, IReadOnlyList<string> Tools)
{
    public static PersonaDto FromDomain(AgentPersona p) =>
        new(p.Id, p.Name, p.Backend.Value, p.Version.Value, p.Tools.Select(t => t.Qualified).ToArray());
}

public sealed record RunDto(
    Guid Id,
    string WorkflowId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? InputSummary)
{
    public static RunDto FromDomain(Run r) =>
        new(r.Id, r.WorkflowId, r.Status.ToString(), r.StartedAt, r.EndedAt, r.InputSummary);
}

public sealed record StartRunRequest(string WorkflowId, string UserInput, string? ThreadId = null);

public sealed record StartRunResponse(Guid RunId, string Status, string EventsUrl);

public sealed record MessageDto(
    Guid Id,
    Guid RunId,
    string Role,
    string Content,
    string? AgentId,
    int SequenceNo,
    DateTimeOffset CreatedAt)
{
    public static MessageDto FromDomain(Message m) =>
        new(m.Id, m.RunId, m.Role.ToString(), m.Content, m.AgentId, m.SequenceNo, m.CreatedAt);
}
