using CloudEngAgent.Application.Runs;
using CloudEngAgent.Domain.Runs;
using CloudEngAgent.Domain.Workflows;

namespace CloudEngAgent.Application.Abstractions;

public interface IWorkflowEngine
{
    IAsyncEnumerable<RunEvent> ExecuteAsync(
        WorkflowDefinition workflow,
        StartWorkflowRunInput input,
        Guid runId,
        CancellationToken cancellationToken);
}
