using System.Runtime.CompilerServices;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Domain.Inputs;
using CloudEngAgent.Infrastructure.Persistence;
using CloudEngAgent.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CloudEngAgent.Infrastructure.Inputs;

public sealed class InputRequestStore(IDbContextFactory<RunsDbContext> dbContextFactory) : IInputRequestStore
{
    public async Task AddAsync(InputRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var ctx = await dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        ctx.InputRequests.Add(ToEntity(request));
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<InputRequest?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var ctx = await dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await ctx.InputRequests.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        return entity is null ? null : ToDomain(entity);
    }

    public async IAsyncEnumerable<InputRequest> ListPendingAsync(
        Guid runId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var ctx = await dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await foreach (var entity in ctx.InputRequests.AsNoTracking()
            .Where(e => e.RunId == runId && e.Status == (int)InputRequestStatus.Pending)
            .OrderBy(e => e.CreatedAt)
            .AsAsyncEnumerable()
            .WithCancellation(ct)
            .ConfigureAwait(false))
        {
            yield return ToDomain(entity);
        }
    }

    public async Task<SubmitInputResult> SubmitAsync(
        Guid id,
        string payloadJson,
        DateTimeOffset now,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payloadJson);

        // Retry once on optimistic concurrency conflict.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var ctx = await dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var entity = await ctx.InputRequests
                .FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);

            if (entity is null)
            {
                return new SubmitInputResult.NotFound();
            }

            var status = (InputRequestStatus)entity.Status;

            if (status == InputRequestStatus.Accepted)
            {
                if (string.Equals(entity.PayloadJson, payloadJson, StringComparison.Ordinal))
                {
                    return new SubmitInputResult.AlreadyAccepted(ToDomain(entity));
                }

                return new SubmitInputResult.Conflict(
                    ToDomain(entity),
                    "already accepted with different payload");
            }

            if (status == InputRequestStatus.Pending && now >= entity.ExpiresAt)
            {
                entity.Status = (int)InputRequestStatus.Expired;
                try
                {
                    await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // best-effort: someone else mutated it; just re-read on next iter
                    if (attempt == 0) continue;
                }

                return new SubmitInputResult.Expired(ToDomain(entity));
            }

            if (status != InputRequestStatus.Pending)
            {
                return new SubmitInputResult.Conflict(ToDomain(entity), status.ToString());
            }

            // Pending and not expired: try to accept.
            entity.Status = (int)InputRequestStatus.Accepted;
            entity.PayloadJson = payloadJson;
            try
            {
                await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
                return new SubmitInputResult.Accepted(ToDomain(entity));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (attempt == 0)
                {
                    continue; // re-read and re-evaluate
                }

                // Final fallback: re-read and decide.
                await using var ctx2 = await dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
                var reread = await ctx2.InputRequests.AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
                if (reread is null) return new SubmitInputResult.NotFound();

                if ((InputRequestStatus)reread.Status == InputRequestStatus.Accepted &&
                    string.Equals(reread.PayloadJson, payloadJson, StringComparison.Ordinal))
                {
                    return new SubmitInputResult.AlreadyAccepted(ToDomain(reread));
                }

                return new SubmitInputResult.Conflict(
                    ToDomain(reread),
                    "concurrent update");
            }
        }

        // Unreachable
        return new SubmitInputResult.Conflict(
            new InputRequest(
                id,
                Guid.NewGuid(),
                "unknown",
                "unknown",
                "unknown",
                InputRequestStatus.Cancelled,
                now,
                now.AddSeconds(1),
                null),
            "exhausted retries");
    }

    private static InputRequestEntity ToEntity(InputRequest r) => new()
    {
        Id = r.Id,
        RunId = r.RunId,
        StepId = r.StepId,
        AgentId = r.AgentId,
        SchemaRef = r.SchemaRef,
        Status = (int)r.Status,
        CreatedAt = r.CreatedAt,
        ExpiresAt = r.ExpiresAt,
        PayloadJson = r.PayloadJson,
    };

    private static InputRequest ToDomain(InputRequestEntity e) => new(
        e.Id,
        e.RunId,
        e.StepId,
        e.AgentId,
        e.SchemaRef,
        (InputRequestStatus)e.Status,
        e.CreatedAt,
        e.ExpiresAt,
        e.PayloadJson);
}
