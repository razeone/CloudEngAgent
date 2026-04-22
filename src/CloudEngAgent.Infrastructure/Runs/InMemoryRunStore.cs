using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Application.Runs;
using CloudEngAgent.Domain.Runs;

namespace CloudEngAgent.Infrastructure.Runs;

/// <summary>
/// In-memory <see cref="IRunStore"/>. Persists runs, events, and messages in
/// per-process dictionaries. Replaced by an EF Core implementation in a later
/// plan; the public surface is identical so consumers do not change.
/// </summary>
public sealed class InMemoryRunStore : IRunStore
{
    private readonly ConcurrentDictionary<Guid, Run> _runs = new();
    private readonly ConcurrentDictionary<Guid, List<RunEvent>> _events = new();
    private readonly ConcurrentDictionary<Guid, List<Message>> _messages = new();
    private readonly object _eventsLock = new();
    private readonly object _messagesLock = new();

    public Task<Run> CreateAsync(Run run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        _runs[run.Id] = run;
        _events.TryAdd(run.Id, new List<RunEvent>());
        _messages.TryAdd(run.Id, new List<Message>());
        return Task.FromResult(run);
    }

    public Task UpdateAsync(Run run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        _runs[run.Id] = run;
        return Task.CompletedTask;
    }

    public Task<Run?> GetAsync(Guid runId, CancellationToken cancellationToken)
        => Task.FromResult(_runs.TryGetValue(runId, out var run) ? run : null);

    public Task AppendEventAsync(RunEvent @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        lock (_eventsLock)
        {
            var list = _events.GetOrAdd(@event.RunId, _ => new List<RunEvent>());
            // Mirror the EF Core unique-index constraint we'll enforce in M2.
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].SequenceNo == @event.SequenceNo)
                {
                    throw new InvalidOperationException(
                        $"Duplicate SequenceNo {@event.SequenceNo} for run {@event.RunId}.");
                }
            }
            list.Add(@event);
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<RunEvent> StreamEventsAsync(
        Guid runId,
        long fromSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_events.TryGetValue(runId, out var list))
        {
            yield break;
        }

        RunEvent[] snapshot;
        lock (_eventsLock)
        {
            snapshot = list
                .Where(e => e.SequenceNo >= fromSequence)
                .OrderBy(e => e.SequenceNo)
                .ToArray();
        }

        foreach (var evt in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return evt;
            await Task.Yield();
        }
    }

    public Task AppendMessageAsync(Message message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var list = _messages.GetOrAdd(message.RunId, _ => new List<Message>());
        lock (_messagesLock)
        {
            list.Add(message);
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<Message> GetMessagesAsync(
        Guid runId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_messages.TryGetValue(runId, out var list))
        {
            yield break;
        }

        Message[] snapshot;
        lock (_messagesLock)
        {
            snapshot = list.ToArray();
        }

        foreach (var msg in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return msg;
            await Task.Yield();
        }
    }

    public async IAsyncEnumerable<Run> ListAsync(
        ListRunsQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var snapshot = _runs.Values
            .Where(r => query.WorkflowId is null || r.WorkflowId == query.WorkflowId)
            .Where(r => query.Status is null || r.Status == query.Status)
            .OrderByDescending(r => r.StartedAt)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToArray();

        foreach (var run in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return run;
            await Task.Yield();
        }
    }
}

/// <summary>
/// In-memory implementation of <see cref="IRunEventBus"/>. Each run has its own
/// unbounded <see cref="Channel{RunEvent}"/>. Subscribers see events published
/// after they subscribe; historical events come from <see cref="IRunStore"/>.
/// </summary>
public sealed class InMemoryRunEventBus : IRunEventBus
{
    private readonly ConcurrentDictionary<Guid, Channel<RunEvent>> _channels = new();

    public ValueTask PublishAsync(RunEvent @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        var channel = _channels.GetOrAdd(@event.RunId, _ => CreateChannel());
        return channel.Writer.WriteAsync(@event, cancellationToken);
    }

    public async IAsyncEnumerable<RunEvent> SubscribeAsync(
        Guid runId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = _channels.GetOrAdd(runId, _ => CreateChannel());
        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return evt;
        }
    }

    public void Complete(Guid runId)
    {
        if (_channels.TryGetValue(runId, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    private static Channel<RunEvent> CreateChannel() => Channel.CreateUnbounded<RunEvent>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = false,
    });
}
