using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Domain.Inputs;
using CloudEngAgent.Domain.Runs;
using CloudEngAgent.Infrastructure.Inputs;
using CloudEngAgent.Infrastructure.Persistence.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CloudEngAgent.Infrastructure.Tests.Inputs;

[Collection("MsSql")]
public sealed class InputRequestStoreTests(MsSqlContainerFixture fixture)
{
    private InputRequestStore CreateStore() =>
        new(fixture.DbContextFactory ?? throw new InvalidOperationException("Fixture not ready."));

    private async Task<Guid> SeedRunAsync()
    {
        var runId = Guid.NewGuid();
        await using var ctx = fixture.DbContextFactory!.CreateDbContext();
        ctx.Runs.Add(new RunEntity
        {
            Id = runId,
            WorkflowId = "test-workflow",
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            EndedAt = null,
            InputSummary = "test",
        });
        await ctx.SaveChangesAsync();
        return runId;
    }

    private static InputRequest NewPending(Guid runId, Guid? id = null, DateTimeOffset? createdAt = null)
    {
        var now = createdAt ?? DateTimeOffset.UtcNow;
        return new InputRequest(
            id ?? Guid.NewGuid(),
            runId,
            "step-1",
            "agent-1",
            "schema://test/v1",
            InputRequestStatus.Pending,
            now,
            now.AddMinutes(5),
            null);
    }

    [Fact]
    public async Task AddAsync_then_GetAsync_round_trips()
    {
        if (fixture.SkipReason is not null) return;

        var store = CreateStore();
        var runId = await SeedRunAsync();
        var req = NewPending(runId);

        await store.AddAsync(req, CancellationToken.None);
        var fetched = await store.GetAsync(req.Id, CancellationToken.None);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(req.Id);
        fetched.RunId.Should().Be(runId);
        fetched.Status.Should().Be(InputRequestStatus.Pending);
    }

    [Fact]
    public async Task AddAsync_with_same_id_twice_does_not_create_duplicate()
    {
        if (fixture.SkipReason is not null) return;

        var store = CreateStore();
        var runId = await SeedRunAsync();
        var id = Guid.NewGuid();
        var req = NewPending(runId, id);

        await store.AddAsync(req, CancellationToken.None);

        var act = async () => await store.AddAsync(NewPending(runId, id), CancellationToken.None);
        await act.Should().ThrowAsync<DbUpdateException>();

        await using var ctx = fixture.DbContextFactory!.CreateDbContext();
        var count = await ctx.InputRequests.CountAsync(e => e.Id == id);
        count.Should().Be(1);
    }

    [Fact]
    public async Task SubmitAsync_accepts_pending_then_is_idempotent_for_same_payload()
    {
        if (fixture.SkipReason is not null) return;

        var store = CreateStore();
        var runId = await SeedRunAsync();
        var req = NewPending(runId);
        await store.AddAsync(req, CancellationToken.None);

        var first = await store.SubmitAsync(req.Id, "{\"a\":1}", DateTimeOffset.UtcNow, CancellationToken.None);
        first.Should().BeOfType<SubmitInputResult.Accepted>();

        var second = await store.SubmitAsync(req.Id, "{\"a\":1}", DateTimeOffset.UtcNow, CancellationToken.None);
        second.Should().BeOfType<SubmitInputResult.AlreadyAccepted>();

        await using var ctx = fixture.DbContextFactory!.CreateDbContext();
        var rows = await ctx.InputRequests.AsNoTracking().Where(e => e.Id == req.Id).ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].Status.Should().Be((int)InputRequestStatus.Accepted);
        rows[0].PayloadJson.Should().Be("{\"a\":1}");
    }

    [Fact]
    public async Task SubmitAsync_returns_Conflict_when_already_accepted_with_different_payload()
    {
        if (fixture.SkipReason is not null) return;

        var store = CreateStore();
        var runId = await SeedRunAsync();
        var req = NewPending(runId);
        await store.AddAsync(req, CancellationToken.None);

        await store.SubmitAsync(req.Id, "{\"a\":1}", DateTimeOffset.UtcNow, CancellationToken.None);
        var conflict = await store.SubmitAsync(req.Id, "{\"a\":2}", DateTimeOffset.UtcNow, CancellationToken.None);

        conflict.Should().BeOfType<SubmitInputResult.Conflict>();
    }

    [Fact]
    public async Task SubmitAsync_returns_NotFound_when_id_unknown()
    {
        if (fixture.SkipReason is not null) return;

        var store = CreateStore();
        var result = await store.SubmitAsync(
            Guid.NewGuid(),
            "{}",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        result.Should().BeOfType<SubmitInputResult.NotFound>();
    }

    [Fact]
    public async Task SubmitAsync_marks_expired_when_past_ExpiresAt()
    {
        if (fixture.SkipReason is not null) return;

        var store = CreateStore();
        var runId = await SeedRunAsync();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var req = NewPending(runId, createdAt: createdAt);
        await store.AddAsync(req, CancellationToken.None);

        var result = await store.SubmitAsync(
            req.Id,
            "{}",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        result.Should().BeOfType<SubmitInputResult.Expired>();
    }

    [Fact]
    public async Task ListPendingAsync_returns_only_pending_for_run()
    {
        if (fixture.SkipReason is not null) return;

        var store = CreateStore();
        var runId = await SeedRunAsync();

        var pending = NewPending(runId);
        var toAccept = NewPending(runId);
        var otherRunId = await SeedRunAsync();
        var otherRunPending = NewPending(otherRunId);

        await store.AddAsync(pending, CancellationToken.None);
        await store.AddAsync(toAccept, CancellationToken.None);
        await store.AddAsync(otherRunPending, CancellationToken.None);

        await store.SubmitAsync(toAccept.Id, "{}", DateTimeOffset.UtcNow, CancellationToken.None);

        var ids = new List<Guid>();
        await foreach (var r in store.ListPendingAsync(runId, CancellationToken.None))
        {
            ids.Add(r.Id);
        }

        ids.Should().ContainSingle().Which.Should().Be(pending.Id);
    }
}
