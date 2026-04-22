using System.Collections.Concurrent;
using System.Text.Json;
using CloudEngAgent.Api.Contracts;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Application.Runs;
using CloudEngAgent.Domain.Runs;
using Microsoft.Extensions.Logging;

namespace CloudEngAgent.Api.Runs;

/// <summary>
/// Owns the lifecycle of an in-flight run: drives
/// <see cref="StartWorkflowRunHandler"/> on a background <see cref="Task"/>,
/// publishes every emitted event to <see cref="IRunEventBus"/>, and tracks a
/// per-run <see cref="CancellationTokenSource"/> so HTTP cancel requests
/// translate into a clean cancellation of the background work.
/// </summary>
public sealed class RunDispatcher(
    IServiceScopeFactory scopeFactory,
    IRunEventBus bus,
    IRunStore store,
    IClock clock,
    ILogger<RunDispatcher> logger)
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _ctsByRun = new();

    public async Task<Guid> StartAsync(StartRunRequest input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var startInput = new StartWorkflowRunInput(
            WorkflowId: input.WorkflowId,
            UserInput: input.UserInput,
            ThreadId: input.ThreadId);

        // The handler creates the run inside its own scope; we need the runId
        // before returning. We do a synchronous handshake by reading the first
        // event (RunStarted) and then continue draining on a background task.
        var scope = scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<StartWorkflowRunHandler>();

        var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
        var stream = handler.HandleAsync(startInput, cts.Token);
        var enumerator = stream.GetAsyncEnumerator(cts.Token);

        bool advanced;
        try
        {
            advanced = await enumerator.MoveNextAsync().ConfigureAwait(false);
        }
        catch
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
            await scope.DisposeAsync().ConfigureAwait(false);
            cts.Dispose();
            throw;
        }

        if (!advanced)
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
            await scope.DisposeAsync().ConfigureAwait(false);
            cts.Dispose();
            throw new InvalidOperationException("Workflow handler produced no events.");
        }

        var first = enumerator.Current;
        var runId = first.RunId;
        _ctsByRun[runId] = cts;
        await bus.PublishAsync(first, CancellationToken.None).ConfigureAwait(false);

        _ = Task.Run(() => DrainAsync(runId, scope, enumerator, cts), CancellationToken.None);

        return runId;
    }

    public bool TryCancel(Guid runId)
    {
        if (_ctsByRun.TryGetValue(runId, out var cts))
        {
            cts.Cancel();
            return true;
        }

        return false;
    }

    private async Task DrainAsync(
        Guid runId,
        AsyncServiceScope scope,
        IAsyncEnumerator<RunEvent> enumerator,
        CancellationTokenSource cts)
    {
        try
        {
            while (await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                await bus.PublishAsync(enumerator.Current, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            await PublishCancellationAsync(runId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run {RunId} dispatcher loop crashed", runId);
            await PublishErrorAsync(runId, ex).ConfigureAwait(false);
        }
        finally
        {
            _ctsByRun.TryRemove(runId, out _);
            cts.Dispose();
            bus.Complete(runId);
            await enumerator.DisposeAsync().ConfigureAwait(false);
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task PublishErrorAsync(Guid runId, Exception ex)
    {
        var existing = await store.GetAsync(runId, CancellationToken.None).ConfigureAwait(false);
        if (existing is { IsTerminal: false })
        {
            await store.UpdateAsync(existing.WithStatus(RunStatus.Failed, clock.UtcNow), CancellationToken.None).ConfigureAwait(false);
        }

        var errorEvent = new RunEvent(
            RunId: runId,
            Type: RunEventType.Error,
            PayloadJson: JsonSerializer.Serialize(new { message = ex.Message, type = ex.GetType().FullName }),
            SequenceNo: long.MaxValue,
            OccurredAt: clock.UtcNow);
        await store.AppendEventAsync(errorEvent, CancellationToken.None).ConfigureAwait(false);
        await bus.PublishAsync(errorEvent, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task PublishCancellationAsync(Guid runId)
    {
        var existing = await store.GetAsync(runId, CancellationToken.None).ConfigureAwait(false);
        if (existing is { IsTerminal: false })
        {
            await store.UpdateAsync(existing.WithStatus(RunStatus.Cancelled, clock.UtcNow), CancellationToken.None).ConfigureAwait(false);
        }
    }
}
