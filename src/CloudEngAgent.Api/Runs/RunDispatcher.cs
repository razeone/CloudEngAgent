using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using CloudEngAgent.Api.Configuration;
using CloudEngAgent.Api.Contracts;
using CloudEngAgent.Api.Observability;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Application.Runs;
using CloudEngAgent.Domain.Runs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    IOptionsMonitor<RunsOptions> runsOptions,
    ILogger<RunDispatcher> logger)
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _ctsByRun = new();
    private int _activeCount;

    public async Task<Guid> StartAsync(StartRunRequest input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var max = runsOptions.CurrentValue.MaxConcurrent;
        if (Interlocked.Increment(ref _activeCount) > max)
        {
            Interlocked.Decrement(ref _activeCount);
            throw new InvalidOperationException(
                $"Maximum run concurrency ({max}) reached. Try again later.");
        }

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
            Interlocked.Decrement(ref _activeCount);
            await enumerator.DisposeAsync().ConfigureAwait(false);
            await scope.DisposeAsync().ConfigureAwait(false);
            cts.Dispose();
            throw;
        }

        if (!advanced)
        {
            Interlocked.Decrement(ref _activeCount);
            await enumerator.DisposeAsync().ConfigureAwait(false);
            await scope.DisposeAsync().ConfigureAwait(false);
            cts.Dispose();
            throw new InvalidOperationException("Workflow handler produced no events.");
        }

        var first = enumerator.Current;
        var runId = first.RunId;
        _ctsByRun[runId] = cts;

        try
        {
            await bus.PublishAsync(first, CancellationToken.None).ConfigureAwait(false);
            Telemetry.EventsPublished.Add(1, new KeyValuePair<string, object?>("type", first.Type.ToString()));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish initial RunStarted event for run {RunId}", runId);
            _ctsByRun.TryRemove(runId, out _);
            Interlocked.Decrement(ref _activeCount);
            try { await enumerator.DisposeAsync().ConfigureAwait(false); } catch { /* observed */ }
            try { await scope.DisposeAsync().ConfigureAwait(false); } catch { /* observed */ }
            try { bus.Complete(runId); } catch { /* observed */ }
            cts.Dispose();
            throw;
        }

        Telemetry.RunsStarted.Add(1, new KeyValuePair<string, object?>("workflow", input.WorkflowId));

        // Ordering: Task.Run is fire-and-forget. The first MoveNextAsync above has
        // *fully completed* (we awaited it) before we schedule DrainAsync, so the
        // enumerator's state machine is quiescent by the time DrainAsync's first
        // MoveNextAsync runs. There is no concurrent access to the enumerator.
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
        using var activity = Telemetry.ActivitySource.StartActivity("run.drain", ActivityKind.Internal);
        activity?.SetTag("run.id", runId);

        var terminalStatus = "Unknown";
        try
        {
            while (await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                try
                {
                    var evt = enumerator.Current;
                    await bus.PublishAsync(evt, CancellationToken.None).ConfigureAwait(false);
                    Telemetry.EventsPublished.Add(1, new KeyValuePair<string, object?>("type", evt.Type.ToString()));
                    if (evt.Type == RunEventType.RunFinished)
                    {
                        terminalStatus = "Succeeded";
                    }
                    else if (evt.Type == RunEventType.Error)
                    {
                        terminalStatus = "Failed";
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to publish event {SequenceNo} for run {RunId}", enumerator.Current.SequenceNo, runId);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            terminalStatus = "Cancelled";
            await SafeAsync(() => PublishCancellationAsync(runId), runId, "cancellation").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            terminalStatus = "Failed";
            logger.LogError(ex, "Run {RunId} dispatcher loop crashed", runId);
            await SafeAsync(() => PublishErrorAsync(runId, ex), runId, "error").ConfigureAwait(false);
        }
        finally
        {
            Telemetry.RunsFinished.Add(1, new KeyValuePair<string, object?>("status", terminalStatus));
            Interlocked.Decrement(ref _activeCount);

            _ctsByRun.TryRemove(runId, out _);

            try { cts.Dispose(); }
            catch (Exception ex) { logger.LogWarning(ex, "Disposing CTS for run {RunId} threw", runId); }

            try { bus.Complete(runId); }
            catch (Exception ex) { logger.LogWarning(ex, "Completing bus for run {RunId} threw", runId); }

            try { await enumerator.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { logger.LogWarning(ex, "Disposing enumerator for run {RunId} threw", runId); }

            try { await scope.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { logger.LogWarning(ex, "Disposing scope for run {RunId} threw", runId); }
        }
    }

    private async Task SafeAsync(Func<Task> action, Guid runId, string label)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish {Label} terminal event for run {RunId}", label, runId);
        }
    }

    private async Task PublishErrorAsync(Guid runId, Exception ex)
    {
        var existing = await store.GetAsync(runId, CancellationToken.None).ConfigureAwait(false);

        var errorEvent = new RunEvent(
            RunId: runId,
            Type: RunEventType.Error,
            PayloadJson: JsonSerializer.Serialize(new { message = ex.Message, type = ex.GetType().FullName }),
            SequenceNo: long.MaxValue,
            OccurredAt: clock.UtcNow);

        if (existing is { IsTerminal: false })
        {
            await store.AppendEventAndUpdateAsync(
                errorEvent,
                existing.WithStatus(RunStatus.Failed, clock.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            await store.AppendEventAsync(errorEvent, CancellationToken.None).ConfigureAwait(false);
        }

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
