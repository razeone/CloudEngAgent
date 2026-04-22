using CloudEngAgent.Domain.Runs;

namespace CloudEngAgent.Application.Abstractions;

/// <summary>
/// Fan-out channel for live <see cref="RunEvent"/>s. Producers (the workflow
/// dispatcher) publish events as they happen; consumers (the AG-UI SSE
/// endpoint) subscribe per-run to receive them in order.
/// </summary>
public interface IRunEventBus
{
    ValueTask PublishAsync(RunEvent @event, CancellationToken cancellationToken);

    IAsyncEnumerable<RunEvent> SubscribeAsync(Guid runId, CancellationToken cancellationToken);

    void Complete(Guid runId);
}
