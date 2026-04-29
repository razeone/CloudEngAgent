namespace CloudEngAgent.Infrastructure.Artifacts;

public sealed class ArtifactStoreOptions
{
    public const string SectionName = "Artifacts";

    public string? Root { get; set; }
}
