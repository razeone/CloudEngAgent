using CloudEngAgent.Domain.Inputs;

namespace CloudEngAgent.Application.Abstractions;

public interface IInputRequestStore
{
    Task AddAsync(InputRequest request, CancellationToken ct);

    Task<InputRequest?> GetAsync(Guid id, CancellationToken ct);

    Task<SubmitInputResult> SubmitAsync(Guid id, string payloadJson, DateTimeOffset now, CancellationToken ct);

    IAsyncEnumerable<InputRequest> ListPendingAsync(Guid runId, CancellationToken ct);
}

public abstract record SubmitInputResult
{
    private SubmitInputResult() { }

    public sealed record Accepted(InputRequest Request) : SubmitInputResult;

    public sealed record AlreadyAccepted(InputRequest Request) : SubmitInputResult;

    public sealed record NotFound : SubmitInputResult;

    public sealed record Expired(InputRequest Request) : SubmitInputResult;

    public sealed record Conflict(InputRequest Request, string Reason) : SubmitInputResult;
}
