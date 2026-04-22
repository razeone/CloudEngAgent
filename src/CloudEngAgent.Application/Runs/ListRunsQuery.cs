using CloudEngAgent.Domain.Runs;

namespace CloudEngAgent.Application.Runs;

public sealed record ListRunsQuery(
    string? WorkflowId = null,
    RunStatus? Status = null,
    int Take = 50,
    int Skip = 0);
