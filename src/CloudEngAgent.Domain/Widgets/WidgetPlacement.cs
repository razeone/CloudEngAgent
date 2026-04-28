namespace CloudEngAgent.Domain.Widgets;

public sealed record WidgetPlacement
{
    public static IReadOnlyList<string> AllowedSurfaces { get; } = new[]
    {
        "chat", "side-panel", "timeline", "artifact-panel",
    };

    public WidgetPlacement(string Surface, string StepId, string AgentId, string? ParentMessageId)
    {
        if (string.IsNullOrWhiteSpace(Surface))
        {
            throw new ArgumentException("Surface must be non-empty.", nameof(Surface));
        }

        if (!AllowedSurfaces.Contains(Surface, StringComparer.Ordinal))
        {
            var known = string.Join(", ", AllowedSurfaces);
            throw new ArgumentException(
                $"Unknown surface '{Surface}'. Allowed: {known}.",
                nameof(Surface));
        }

        if (string.IsNullOrWhiteSpace(StepId))
        {
            throw new ArgumentException("StepId must be non-empty.", nameof(StepId));
        }

        if (string.IsNullOrWhiteSpace(AgentId))
        {
            throw new ArgumentException("AgentId must be non-empty.", nameof(AgentId));
        }

        this.Surface = Surface;
        this.StepId = StepId;
        this.AgentId = AgentId;
        this.ParentMessageId = ParentMessageId;
    }

    public string Surface { get; init; }
    public string StepId { get; init; }
    public string AgentId { get; init; }
    public string? ParentMessageId { get; init; }
}
