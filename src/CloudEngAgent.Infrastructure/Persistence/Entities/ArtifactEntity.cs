namespace CloudEngAgent.Infrastructure.Persistence.Entities;

public sealed class ArtifactEntity
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public int Kind { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string ContentSha256 { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
