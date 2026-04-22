namespace CloudEngAgent.Domain.Runs;

public sealed record Run(
    Guid Id,
    string WorkflowId,
    RunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? InputSummary)
{
    public bool IsTerminal => Status is RunStatus.Succeeded or RunStatus.Failed or RunStatus.Cancelled;

    public Run WithStatus(RunStatus next, DateTimeOffset? endedAt = null) => this with
    {
        Status = next,
        EndedAt = next is RunStatus.Succeeded or RunStatus.Failed or RunStatus.Cancelled
            ? endedAt ?? DateTimeOffset.UtcNow
            : EndedAt,
    };
}
