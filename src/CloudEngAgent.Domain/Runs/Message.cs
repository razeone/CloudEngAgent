namespace CloudEngAgent.Domain.Runs;

public enum MessageRole
{
    User = 0,
    Assistant = 1,
    Tool = 2,
    System = 3,
}

public sealed record Message(
    Guid Id,
    Guid RunId,
    MessageRole Role,
    string Content,
    string? AgentId,
    int SequenceNo,
    DateTimeOffset CreatedAt);
