namespace CloudEngAgent.Infrastructure.Persistence.Entities;

public sealed class RunWidgetStateEntity
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string WidgetKey { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Status { get; set; }
    public int Revision { get; set; }
    public string Placement_Surface { get; set; } = string.Empty;
    public string Placement_StepId { get; set; } = string.Empty;
    public string Placement_AgentId { get; set; } = string.Empty;
    public string? Placement_ParentMessageId { get; set; }
    public string PropsJson { get; set; } = string.Empty;
    public string ArtifactsJson { get; set; } = "[]";
    public DateTimeOffset UpdatedAt { get; set; }
}
