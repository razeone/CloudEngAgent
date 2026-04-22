using CloudEngAgent.Domain.Runs;
using CloudEngAgent.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CloudEngAgent.Infrastructure.Tests;

[Collection("MsSql")]
public sealed class EfCoreRunStoreTests(MsSqlContainerFixture fixture)
{
    private EfCoreRunStore Store =>
        new(fixture.DbContextFactory ?? throw new InvalidOperationException("Fixture not ready."));

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Run NewRun() => new(
        Guid.NewGuid(),
        "test-workflow",
        RunStatus.Running,
        DateTimeOffset.UtcNow,
        EndedAt: null,
        InputSummary: "test");

    private static RunEvent NewEvent(Guid runId, long seq) => new(
        runId,
        RunEventType.TextDelta,
        $"{{\"seq\":{seq}}}",
        seq,
        DateTimeOffset.UtcNow);

    private bool ShouldSkip => fixture.SkipReason is not null;

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_then_GetAsync_round_trip()
    {
        if (ShouldSkip) return;
        var store = Store;
        var run = NewRun();

        await store.CreateAsync(run, CancellationToken.None);
        var fetched = await store.GetAsync(run.Id, CancellationToken.None);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(run.Id);
        fetched.WorkflowId.Should().Be(run.WorkflowId);
        fetched.Status.Should().Be(run.Status);
        fetched.InputSummary.Should().Be(run.InputSummary);
    }

    [Fact]
    public async Task AppendEventAsync_rejects_duplicate_sequence_no()
    {
        if (ShouldSkip) return;
        var store = Store;
        var run = NewRun();
        await store.CreateAsync(run, CancellationToken.None);

        await store.AppendEventAsync(NewEvent(run.Id, 1), CancellationToken.None);

        var act = async () =>
            await store.AppendEventAsync(NewEvent(run.Id, 1), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Duplicate SequenceNo 1*");
    }

    [Fact]
    public async Task StreamEventsAsync_returns_events_in_sequence_order()
    {
        if (ShouldSkip) return;
        var store = Store;
        var run = NewRun();
        await store.CreateAsync(run, CancellationToken.None);

        // Insert out of order: 3, 1, 2
        await store.AppendEventAsync(NewEvent(run.Id, 3), CancellationToken.None);
        await store.AppendEventAsync(NewEvent(run.Id, 1), CancellationToken.None);
        await store.AppendEventAsync(NewEvent(run.Id, 2), CancellationToken.None);

        var events = new List<RunEvent>();
        await foreach (var e in store.StreamEventsAsync(run.Id, 0, CancellationToken.None))
            events.Add(e);

        events.Select(e => e.SequenceNo).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task StreamEventsAsync_filters_by_from_sequence()
    {
        if (ShouldSkip) return;
        var store = Store;
        var run = NewRun();
        await store.CreateAsync(run, CancellationToken.None);

        for (var i = 1; i <= 5; i++)
            await store.AppendEventAsync(NewEvent(run.Id, i), CancellationToken.None);

        var events = new List<RunEvent>();
        await foreach (var e in store.StreamEventsAsync(run.Id, 3, CancellationToken.None))
            events.Add(e);

        events.Select(e => e.SequenceNo).Should().Equal(3, 4, 5);
    }

    [Fact]
    public async Task Cascade_delete_run_removes_events()
    {
        if (ShouldSkip) return;
        var store = Store;
        var run = NewRun();
        await store.CreateAsync(run, CancellationToken.None);

        for (var i = 1; i <= 3; i++)
            await store.AppendEventAsync(NewEvent(run.Id, i), CancellationToken.None);

        // Delete the run directly via DbContext (bypasses store abstraction)
        await using var ctx = fixture.DbContextFactory!.CreateDbContext();
        var entity = await ctx.Runs.FindAsync(run.Id);
        entity.Should().NotBeNull();
        ctx.Runs.Remove(entity!);
        await ctx.SaveChangesAsync();

        // Cascade should have removed the events
        var count = await ctx.RunEvents.CountAsync(e => e.RunId == run.Id);
        count.Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_appends_serialize_correctly()
    {
        if (ShouldSkip) return;
        var store = Store;
        var run = NewRun();
        await store.CreateAsync(run, CancellationToken.None);

        // Part 1: 20 distinct sequence numbers all land
        var distinctTasks = Enumerable.Range(1, 20)
            .Select(i => store.AppendEventAsync(NewEvent(run.Id, i), CancellationToken.None))
            .ToArray();
        await Task.WhenAll(distinctTasks);

        await using var ctx1 = fixture.DbContextFactory!.CreateDbContext();
        var count = await ctx1.RunEvents.CountAsync(e => e.RunId == run.Id);
        count.Should().Be(20);

        // Part 2: 20 tasks all using seq=21 → exactly 1 succeeds, 19 throw
        var duplicateTasks = Enumerable.Range(0, 20)
            .Select(_ => store.AppendEventAsync(NewEvent(run.Id, 21), CancellationToken.None)
                .ContinueWith(t => t.Exception?.InnerException, TaskContinuationOptions.None))
            .ToArray();
        var exceptions = await Task.WhenAll(duplicateTasks);

        var successCount = exceptions.Count(e => e is null);
        var failCount = exceptions.Count(e => e is InvalidOperationException);

        successCount.Should().Be(1);
        failCount.Should().Be(19);
    }
}
