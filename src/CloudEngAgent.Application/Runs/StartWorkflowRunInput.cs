namespace CloudEngAgent.Application.Runs;

public sealed record StartWorkflowRunInput(
    string WorkflowId,
    string UserInput,
    string? ThreadId = null,
    string? RequestedBy = null);
