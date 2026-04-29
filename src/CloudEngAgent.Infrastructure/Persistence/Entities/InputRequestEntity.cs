namespace CloudEngAgent.Infrastructure.Persistence.Entities;

public sealed class InputRequestEntity
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string StepId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string SchemaRef { get; set; } = string.Empty;
    public int Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string? PayloadJson { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
