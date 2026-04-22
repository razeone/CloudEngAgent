using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Domain.Workflows;

namespace CloudEngAgent.Infrastructure.Workflows;

/// <summary>
/// Seed implementation of <see cref="IWorkflowRegistry"/>. Registers a single
/// <c>dba-default</c> workflow whose entry persona is the orchestrator.
/// </summary>
public sealed class InMemoryWorkflowRegistry : IWorkflowRegistry
{
    private readonly Dictionary<string, WorkflowDefinition> _workflows;

    public InMemoryWorkflowRegistry()
    {
        var personas = new[]
        {
            "orchestrator",
            "explorer",
            "analyst",
            "performance",
            "assessor",
            "administrator",
        };

        var dbaDefault = new WorkflowDefinition(
            Id: "dba-default",
            Name: "DBA Default Workflow",
            PersonaIds: personas,
            EntryPersonaId: "orchestrator");

        _workflows = new Dictionary<string, WorkflowDefinition>(StringComparer.Ordinal)
        {
            [dbaDefault.Id] = dbaDefault,
        };
    }

    public WorkflowDefinition? Get(string workflowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        return _workflows.TryGetValue(workflowId, out var w) ? w : null;
    }

    public IReadOnlyList<WorkflowDefinition> List() => _workflows.Values.ToArray();
}
