namespace CloudEngAgent.Domain.Widgets;

public sealed record ArtifactRef(
    Guid ArtifactId,
    string Filename,
    string ContentType,
    long SizeBytes);

public sealed record WidgetState
{
    public WidgetState(
        WidgetKey Key,
        WidgetType Type,
        WidgetStatus Status,
        int Revision,
        WidgetPlacement Placement,
        string PropsJson,
        IReadOnlyList<ArtifactRef> Artifacts)
    {
        ArgumentNullException.ThrowIfNull(Key);
        ArgumentNullException.ThrowIfNull(Placement);
        ArgumentNullException.ThrowIfNull(PropsJson);
        ArgumentNullException.ThrowIfNull(Artifacts);

        if (Revision <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Revision),
                Revision,
                "Revision must be a positive integer (monotonic, starts at 1).");
        }

        this.Key = Key;
        this.Type = Type;
        this.Status = Status;
        this.Revision = Revision;
        this.Placement = Placement;
        this.PropsJson = PropsJson;
        this.Artifacts = Artifacts;
    }

    public WidgetKey Key { get; init; }
    public WidgetType Type { get; init; }
    public WidgetStatus Status { get; init; }
    public int Revision { get; init; }
    public WidgetPlacement Placement { get; init; }
    public string PropsJson { get; init; }
    public IReadOnlyList<ArtifactRef> Artifacts { get; init; }
}
