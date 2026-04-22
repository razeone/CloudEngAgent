using CloudEngAgent.Domain.Runs;

namespace CloudEngAgent.Infrastructure.Persistence.Entities;

public sealed class RunEventEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public RunEventType Type { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public long SequenceNo { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    public RunEntity Run { get; set; } = null!;
}
