using CloudEngAgent.Domain.Runs;

namespace CloudEngAgent.Infrastructure.Persistence.Entities;

public sealed class MessageEntity
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? AgentId { get; set; }
    public int SequenceNo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public RunEntity Run { get; set; } = null!;
}
