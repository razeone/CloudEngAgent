namespace CloudEngAgent.Domain.Inputs;

public enum InputRequestStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2,
    Expired = 3,
    Cancelled = 4,
}

public sealed record InputRequest
{
    public InputRequest(
        Guid Id,
        Guid RunId,
        string StepId,
        string AgentId,
        string SchemaRef,
        InputRequestStatus Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt,
        string? PayloadJson)
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("Id must be a non-empty Guid.", nameof(Id));
        }

        if (RunId == Guid.Empty)
        {
            throw new ArgumentException("RunId must be a non-empty Guid.", nameof(RunId));
        }

        if (string.IsNullOrWhiteSpace(StepId))
        {
            throw new ArgumentException("StepId must be non-empty.", nameof(StepId));
        }

        if (string.IsNullOrWhiteSpace(AgentId))
        {
            throw new ArgumentException("AgentId must be non-empty.", nameof(AgentId));
        }

        if (string.IsNullOrWhiteSpace(SchemaRef))
        {
            throw new ArgumentException("SchemaRef must be non-empty.", nameof(SchemaRef));
        }

        if (ExpiresAt <= CreatedAt)
        {
            throw new ArgumentException(
                "ExpiresAt must be strictly after CreatedAt.",
                nameof(ExpiresAt));
        }

        this.Id = Id;
        this.RunId = RunId;
        this.StepId = StepId;
        this.AgentId = AgentId;
        this.SchemaRef = SchemaRef;
        this.Status = Status;
        this.CreatedAt = CreatedAt;
        this.ExpiresAt = ExpiresAt;
        this.PayloadJson = PayloadJson;
    }

    public Guid Id { get; init; }
    public Guid RunId { get; init; }
    public string StepId { get; init; }
    public string AgentId { get; init; }
    public string SchemaRef { get; init; }
    public InputRequestStatus Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public string? PayloadJson { get; init; }

    public InputRequest Accept(string payloadJson, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(payloadJson);

        if (Status != InputRequestStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Cannot accept input request {Id}: status is {Status}, expected Pending.");
        }

        if (now >= ExpiresAt)
        {
            throw new InvalidOperationException(
                $"Cannot accept input request {Id}: expired at {ExpiresAt:O} (now {now:O}).");
        }

        return this with
        {
            Status = InputRequestStatus.Accepted,
            PayloadJson = payloadJson,
        };
    }

    public InputRequest Reject(DateTimeOffset now)
    {
        if (Status != InputRequestStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Cannot reject input request {Id}: status is {Status}, expected Pending.");
        }

        _ = now;
        return this with { Status = InputRequestStatus.Rejected };
    }

    public InputRequest Expire(DateTimeOffset now)
    {
        if (Status != InputRequestStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Cannot expire input request {Id}: status is {Status}, expected Pending.");
        }

        _ = now;
        return this with { Status = InputRequestStatus.Expired };
    }
}
