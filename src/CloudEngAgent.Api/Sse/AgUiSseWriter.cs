using System.Globalization;
using System.Text;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Domain.Runs;
using Microsoft.AspNetCore.Http;

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

    public static async Task WriteAsync(
        HttpContext context,
        Guid runId,
        IRunStore store,
        IRunEventBus bus,
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
                    return;
                }
            }

            // Heartbeat loop interleaved with live event consumption.
            using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(15));
            var moveNext = liveEnumerator.MoveNextAsync().AsTask();

            while (!cancellationToken.IsCancellationRequested)
            {
                var tick = heartbeat.WaitForNextTickAsync(cancellationToken).AsTask();
                var winner = await Task.WhenAny(moveNext, tick).ConfigureAwait(false);

                if (winner == moveNext)
                {
                    if (!await moveNext.ConfigureAwait(false))
                    {
                        return;
                    }

                    var evt = liveEnumerator.Current;
                    if (seenSequences.Add(evt.SequenceNo) &&
                        await WriteEventAsync(context, evt, runId, threadId).ConfigureAwait(false))
                    {
                        return;
                    }

                    moveNext = liveEnumerator.MoveNextAsync().AsTask();
                }
                else
                {
                    await context.Response.WriteAsync(TimerComment, cancellationToken).ConfigureAwait(false);
                    await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected.
        }
        finally
        {
            await liveEnumerator.DisposeAsync().ConfigureAwait(false);
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
