using CloudEngAgent.Domain.Runs;

namespace CloudEngAgent.Infrastructure.Persistence.Entities;

public sealed class RunEntity
{
    public Guid Id { get; set; }
    public string WorkflowId { get; set; } = string.Empty;
    public RunStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? InputSummary { get; set; }

    public List<RunEventEntity> Events { get; set; } = [];
    public List<MessageEntity> Messages { get; set; } = [];
}
