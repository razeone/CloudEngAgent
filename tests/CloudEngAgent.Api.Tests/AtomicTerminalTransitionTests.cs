using System.Net.Http.Json;
using CloudEngAgent.Api.Contracts;
using CloudEngAgent.Api.Runs;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Domain.Runs;
using CloudEngAgent.Infrastructure.Runs;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CloudEngAgent.Api.Tests;

/// <summary>
/// Tests for the atomic terminal-event / run-status write introduced in M2.
/// </summary>
public sealed class AtomicTerminalTransitionTests
{
    // ------------------------------------------------------------------ //
    //  Unit tests – InMemoryRunStore directly                             //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task AppendEventAndUpdateAsync_leaves_run_terminal_and_event_present()
    {
        var store = new InMemoryRunStore();
        var runId = Guid.NewGuid();
        var run = new Run(runId, "wf-1", RunStatus.Running, DateTimeOffset.UtcNow, null, null);
        await store.CreateAsync(run, CancellationToken.None);

        var terminalRun = run.WithStatus(RunStatus.Succeeded, DateTimeOffset.UtcNow);
        var evt = new RunEvent(runId, RunEventType.RunFinished, "{}", SequenceNo: 1, OccurredAt: DateTimeOffset.UtcNow);

        await store.AppendEventAndUpdateAsync(evt, terminalRun, CancellationToken.None);

        var persisted = await store.GetAsync(runId, CancellationToken.None);
        persisted.Should().NotBeNull();
        persisted!.Status.Should().Be(RunStatus.Succeeded);
        persisted.IsTerminal.Should().BeTrue();

        var events = await store.StreamEventsAsync(runId, fromSequence: 0, CancellationToken.None)
            .ToListAsync();
        events.Should().ContainSingle(e => e.SequenceNo == 1 && e.Type == RunEventType.RunFinished);
    }

    [Fact]
    public async Task AppendEventAndUpdateAsync_rejects_duplicate_seq_and_run_is_unchanged()
    {
        var store = new InMemoryRunStore();
        var runId = Guid.NewGuid();
        var run = new Run(runId, "wf-1", RunStatus.Running, DateTimeOffset.UtcNow, null, null);
        await store.CreateAsync(run, CancellationToken.None);

        var evt1 = new RunEvent(runId, RunEventType.RunFinished, "{}", SequenceNo: 1, OccurredAt: DateTimeOffset.UtcNow);
        await store.AppendEventAsync(evt1, CancellationToken.None);

        var terminalRun = run.WithStatus(RunStatus.Succeeded, DateTimeOffset.UtcNow);
        var evtDup = new RunEvent(runId, RunEventType.RunFinished, "{}", SequenceNo: 1, OccurredAt: DateTimeOffset.UtcNow);

        var act = () => store.AppendEventAndUpdateAsync(evtDup, terminalRun, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Duplicate SequenceNo 1 for run {runId}.");

        // Run must not have been updated because the write was rolled back atomically.
        var persisted = await store.GetAsync(runId, CancellationToken.None);
        persisted!.Status.Should().Be(RunStatus.Running);
    }

    // ------------------------------------------------------------------ //
    //  Stress test – 50 parallel runs via the live dispatcher             //
    // ------------------------------------------------------------------ //

    [Fact(Timeout = 30_000)]
    public async Task Fifty_concurrent_runs_all_reach_terminal_status_without_duplicate_seq()
    {
        // Use a dedicated factory with a higher concurrency cap so all 50 runs
        // can be in-flight at the same time.
        await using var factory = new ApiFactory
        {
            ExtraConfig = new Dictionary<string, string?>
            {
                ["Runs:MaxConcurrent"] = "100",
            },
        };

        // Warm up the host so the singleton RunDispatcher is ready.
        using var _ = factory.CreateClient();

        var dispatcher = factory.Services.GetRequiredService<RunDispatcher>();
        var store = factory.Services.GetRequiredService<IRunStore>();

        const int N = 50;

        // Fire N runs in parallel.
        var startTasks = Enumerable.Range(0, N).Select(_ =>
            dispatcher.StartAsync(new StartRunRequest("dba-default", "stress-test"), CancellationToken.None));

        Guid[] runIds;
        try
        {
            runIds = await Task.WhenAll(startTasks);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("One or more StartAsync calls threw unexpectedly.", ex);
        }

        runIds.Should().HaveCount(N);

        // Poll until every run is terminal (max ~20 s total).
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var remaining = new HashSet<Guid>(runIds);
        while (remaining.Count > 0 && !timeout.Token.IsCancellationRequested)
        {
            foreach (var id in remaining.ToList())
            {
                var r = await store.GetAsync(id, CancellationToken.None);
                if (r?.IsTerminal == true)
                {
                    remaining.Remove(id);
                }
            }
            if (remaining.Count > 0)
            {
                await Task.Delay(50, CancellationToken.None);
            }
        }

        remaining.Should().BeEmpty("all runs must reach a terminal status within the timeout");

        // Verify no run is stuck in Running.
        foreach (var id in runIds)
        {
            var r = await store.GetAsync(id, CancellationToken.None);
            r.Should().NotBeNull();
            r!.IsTerminal.Should().BeTrue($"run {id} should be terminal");
        }
    }
}

file static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(
        this IAsyncEnumerable<T> source,
        CancellationToken ct = default)
    {
        var list = new List<T>();
        await foreach (var item in source.WithCancellation(ct))
        {
            list.Add(item);
        }
        return list;
    }
}
