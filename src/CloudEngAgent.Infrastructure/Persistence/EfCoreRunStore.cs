using System.Runtime.CompilerServices;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Application.Runs;
using CloudEngAgent.Domain.Runs;
using CloudEngAgent.Infrastructure.Persistence.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CloudEngAgent.Infrastructure.Persistence;

public sealed class EfCoreRunStore(IDbContextFactory<RunsDbContext> dbContextFactory) : IRunStore
{
    public async Task<Run> CreateAsync(Run run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var ctx = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        ctx.Runs.Add(Mappers.ToEntity(run));
        await ctx.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task UpdateAsync(Run run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await using var ctx = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        ctx.Runs.Update(Mappers.ToEntity(run));
        await ctx.SaveChangesAsync(cancellationToken);
    }

    public async Task<Run?> GetAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var ctx = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await ctx.Runs.FindAsync([runId], cancellationToken);
        return entity is null ? null : Mappers.ToDomain(entity);
    }

    public async Task AppendEventAsync(RunEvent @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        try
        {
            await using var ctx = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            ctx.RunEvents.Add(Mappers.ToEntity(@event));
            await ctx.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql
            && (sql.Number == 2601 || sql.Number == 2627))
        {
            throw new InvalidOperationException(
                $"Duplicate SequenceNo {@event.SequenceNo} for run {@event.RunId}.");
        }
    }

    public async Task AppendEventAndUpdateAsync(RunEvent evt, Run updated, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(updated);
        try
        {
            await using var ctx = await dbContextFactory.CreateDbContextAsync(ct);
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            ctx.RunEvents.Add(Mappers.ToEntity(evt));
            ctx.Runs.Update(Mappers.ToEntity(updated));
            await ctx.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql
            && (sql.Number == 2601 || sql.Number == 2627))
        {
            throw new InvalidOperationException(
                $"Duplicate SequenceNo {evt.SequenceNo} for run {evt.RunId}.");
        }
    }

    public async IAsyncEnumerable<RunEvent> StreamEventsAsync(
        Guid runId,
        long fromSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var ctx = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await foreach (var entity in ctx.RunEvents
            .Where(e => e.RunId == runId && e.SequenceNo >= fromSequence)
            .OrderBy(e => e.SequenceNo)
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            yield return Mappers.ToDomain(entity);
        }
    }

    public async Task AppendMessageAsync(Message message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        await using var ctx = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        ctx.Messages.Add(Mappers.ToEntity(message));
        await ctx.SaveChangesAsync(cancellationToken);
    }

    public async IAsyncEnumerable<Message> GetMessagesAsync(
        Guid runId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var ctx = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await foreach (var entity in ctx.Messages
            .Where(m => m.RunId == runId)
            .OrderBy(m => m.SequenceNo)
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            yield return Mappers.ToDomain(entity);
        }
    }

    public async IAsyncEnumerable<Run> ListAsync(
        ListRunsQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var ctx = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var q = ctx.Runs.AsQueryable();
        if (query.WorkflowId is not null)
            q = q.Where(r => r.WorkflowId == query.WorkflowId);
        if (query.Status is not null)
            q = q.Where(r => r.Status == query.Status);

        await foreach (var entity in q
            .OrderByDescending(r => r.StartedAt)
            .Skip(query.Skip)
            .Take(query.Take)
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            yield return Mappers.ToDomain(entity);
        }
    }
}
