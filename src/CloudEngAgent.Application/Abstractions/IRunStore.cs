using CloudEngAgent.Application.Runs;
using CloudEngAgent.Domain.Runs;

namespace CloudEngAgent.Application.Abstractions;

public interface IRunStore
{
    Task<Run> CreateAsync(Run run, CancellationToken cancellationToken);

    Task UpdateAsync(Run run, CancellationToken cancellationToken);

    Task<Run?> GetAsync(Guid runId, CancellationToken cancellationToken);

    Task AppendEventAsync(RunEvent @event, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically appends <paramref name="evt"/> and replaces the persisted
    /// <see cref="Run"/> with <paramref name="updated"/> in a single commit.
    /// Use for terminal transitions so the event and status change are never
    /// observed independently.
    /// </summary>
    Task AppendEventAndUpdateAsync(RunEvent evt, Run updated, CancellationToken ct);

    IAsyncEnumerable<RunEvent> StreamEventsAsync(Guid runId, long fromSequence, CancellationToken cancellationToken);

    Task AppendMessageAsync(Message message, CancellationToken cancellationToken);

    IAsyncEnumerable<Message> GetMessagesAsync(Guid runId, CancellationToken cancellationToken);

    IAsyncEnumerable<Run> ListAsync(ListRunsQuery query, CancellationToken cancellationToken);
}
