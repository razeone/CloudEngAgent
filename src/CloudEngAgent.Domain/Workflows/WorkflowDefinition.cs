namespace CloudEngAgent.Domain.Workflows;

public sealed record WorkflowDefinition(
    string Id,
    string Name,
    IReadOnlyList<string> PersonaIds,
    string EntryPersonaId);
