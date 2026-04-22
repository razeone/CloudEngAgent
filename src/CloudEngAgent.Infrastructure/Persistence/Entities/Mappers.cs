using CloudEngAgent.Domain.Runs;

namespace CloudEngAgent.Infrastructure.Persistence.Entities;

internal static class Mappers
{
    public static RunEntity ToEntity(Run run) => new()
    {
        Id = run.Id,
        WorkflowId = run.WorkflowId,
        Status = run.Status,
        StartedAt = run.StartedAt,
        EndedAt = run.EndedAt,
        InputSummary = run.InputSummary,
    };

    public static Run ToDomain(RunEntity entity) => new(
        entity.Id,
        entity.WorkflowId,
        entity.Status,
        entity.StartedAt,
        entity.EndedAt,
        entity.InputSummary);

    public static RunEventEntity ToEntity(RunEvent evt) => new()
    {
        Id = Guid.NewGuid(),
        RunId = evt.RunId,
        Type = evt.Type,
        PayloadJson = evt.PayloadJson,
        SequenceNo = evt.SequenceNo,
        OccurredAt = evt.OccurredAt,
    };

    public static RunEvent ToDomain(RunEventEntity entity) => new(
        entity.RunId,
        entity.Type,
        entity.PayloadJson,
        entity.SequenceNo,
        entity.OccurredAt);

    public static MessageEntity ToEntity(Message msg) => new()
    {
        Id = msg.Id,
        RunId = msg.RunId,
        Role = msg.Role,
        Content = msg.Content,
        AgentId = msg.AgentId,
        SequenceNo = msg.SequenceNo,
        CreatedAt = msg.CreatedAt,
    };

    public static Message ToDomain(MessageEntity entity) => new(
        entity.Id,
        entity.RunId,
        entity.Role,
        entity.Content,
        entity.AgentId,
        entity.SequenceNo,
        entity.CreatedAt);
}
