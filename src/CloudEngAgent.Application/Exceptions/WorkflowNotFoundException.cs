namespace CloudEngAgent.Application.Exceptions;

public sealed class WorkflowNotFoundException(string workflowId)
    : Exception($"Workflow '{workflowId}' is not registered.")
{
    public string WorkflowId { get; } = workflowId;
}
