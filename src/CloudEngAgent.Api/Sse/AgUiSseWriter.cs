using System.Globalization;
using System.Text;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Domain.Runs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CloudEngAgent.Api.Sse;

/// <summary>
/// Writes an AG-UI compliant Server-Sent Events stream for a single run.
///
/// Replays persisted <see cref="RunEvent"/>s from <see cref="IRunStore"/>
/// (honoring the <c>Last-Event-ID</c> request header) and then subscribes
/// to <see cref="IRunEventBus"/> for live updates. Each <see cref="RunEvent"/>
/// is mapped to one or more AG-UI events by <see cref="AgUiEventMapper"/>.
/// </summary>
internal static class AgUiSseWriter
{
    private const string TimerComment = ": ping\n\n";
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    public static async Task WriteAsync(
        HttpContext context,
        Guid runId,
        IRunStore store,
        IRunEventBus bus,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(bus);

        context.Response.Headers["Content-Type"] = "text/event-stream";
        context.Response.Headers["Cache-Control"] = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        var fromSequence = TryParseLastEventId(context.Request.Headers["Last-Event-ID"]);
        var seenSequences = new HashSet<long>();
        var threadId = context.Request.Query.TryGetValue("threadId", out var tid) ? tid.ToString() : null;

        // Subscribe before replay so live events queued during replay are not lost.
        var liveStream = bus.SubscribeAsync(runId, cancellationToken);
        var liveEnumerator = liveStream.GetAsyncEnumerator(cancellationToken);

        // Heartbeat task lifetime is bound to this CTS so we can cancel and
        // synchronously observe the pending tick before disposing the timer.
        // Otherwise the orphan task throws ObjectDisposedException/InvalidOperationException
        // on the thread pool when the using-block disposes the timer.
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = new PeriodicTimer(HeartbeatInterval);
        Task<bool> tickTask = WaitTickAsync(heartbeat, heartbeatCts.Token);
        Task<bool> moveNext = Task.FromResult(false); // assigned before loop entry below

        var sawTerminal = false;
        try
        {
            await foreach (var evt in store.StreamEventsAsync(runId, fromSequence, cancellationToken)
                .ConfigureAwait(false))
            {
                if (!seenSequences.Add(evt.SequenceNo))
                {
                    continue;
                }

                if (await WriteEventAsync(context, evt, runId, threadId).ConfigureAwait(false))
                {
                    sawTerminal = true;
                    return;
                }
            }

            moveNext = liveEnumerator.MoveNextAsync().AsTask();

            while (!cancellationToken.IsCancellationRequested)
            {
                var winner = await Task.WhenAny(moveNext, tickTask).ConfigureAwait(false);

                if (winner == moveNext)
                {
                    bool advanced;
                    try
                    {
                        advanced = await moveNext.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    if (!advanced)
                    {
                        // Bus channel completed. If we never saw a terminal event in
                        // either replay or live stream, surface a warning – this means
                        // the run ended without producing RunFinished/RunError, which
                        // is a bug in the producer (or a Complete-vs-subscribe race
                        // that the producer should fix by writing a terminal first).
                        if (!sawTerminal)
                        {
                            logger?.LogWarning(
                                "SSE stream for run {RunId} ended without a terminal event. " +
                                "The producer completed the bus channel before publishing RunFinished/RunError.",
                                runId);
                        }

                        return;
                    }

                    var evt = liveEnumerator.Current;
                    if (seenSequences.Add(evt.SequenceNo) &&
                        await WriteEventAsync(context, evt, runId, threadId).ConfigureAwait(false))
                    {
                        sawTerminal = true;
                        return;
                    }

                    moveNext = liveEnumerator.MoveNextAsync().AsTask();
                }
                else
                {
                    bool ok;
                    try
                    {
                        ok = await tickTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    if (!ok)
                    {
                        return;
                    }

                    await context.Response.WriteAsync(TimerComment, cancellationToken).ConfigureAwait(false);
                    await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

                    tickTask = WaitTickAsync(heartbeat, heartbeatCts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected or shutdown requested.
        }
        finally
        {
            // Cancel the heartbeat first so the pending tick task completes,
            // observe any exception from it (swallow), THEN dispose the timer
            // and the live enumerator. Doing this in order prevents orphan tasks.
            heartbeatCts.Cancel();
            try
            {
                await tickTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }

            heartbeat.Dispose();

            try
            {
                // Observe the pending live MoveNext if any, so it doesn't leak.
                _ = await moveNext.ConfigureAwait(false);
            }
            catch
            {
                // Swallow – the request is over and we've already returned to the framework.
            }

            await liveEnumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<bool> WaitTickAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns <c>true</c> if the stream should be closed after this event
    /// (terminal AG-UI events: <c>RunFinished</c>, <c>RunError</c>).
    /// </summary>
    private static async Task<bool> WriteEventAsync(HttpContext context, RunEvent evt, Guid runId, string? threadId)
    {
        var frames = AgUiEventMapper.Map(evt, runId, threadId);
        if (frames.Count == 0)
        {
            return false;
        }

        var sb = new StringBuilder(256);
        var terminal = false;
        foreach (var (eventType, data) in frames)
        {
            sb.Append("id: ").Append(evt.SequenceNo.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("event: ").Append(eventType).Append('\n');
            sb.Append("data: ").Append(data).Append("\n\n");
            terminal |= eventType is "RunFinished" or "RunError";
        }

        await context.Response.WriteAsync(sb.ToString(), context.RequestAborted).ConfigureAwait(false);
        await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
        return terminal;
    }

    private static long TryParseLastEventId(Microsoft.Extensions.Primitives.StringValues header)
    {
        if (header.Count == 0)
        {
            return 0;
        }

        return long.TryParse(header.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq) && seq >= 0
            ? seq + 1
            : 0;
    }
}
