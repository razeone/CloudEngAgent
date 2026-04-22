using CloudEngAgent.Domain.Workflows;

namespace CloudEngAgent.Application.Abstractions;

public interface IWorkflowRegistry
{
    WorkflowDefinition? Get(string workflowId);

    IReadOnlyList<WorkflowDefinition> List();
}
