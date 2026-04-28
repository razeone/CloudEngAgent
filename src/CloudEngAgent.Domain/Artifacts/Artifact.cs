using System.Text.RegularExpressions;

namespace CloudEngAgent.Domain.Artifacts;

public enum ArtifactKind
{
    MarkdownReport = 0,
    Pdf = 1,
    Csv = 2,
    Json = 3,
    Other = 4,
}

public sealed partial record Artifact
{
    private static readonly Regex Sha256Pattern = BuildSha256Regex();

    public Artifact(
        Guid Id,
        Guid RunId,
        ArtifactKind Kind,
        string Filename,
        string ContentType,
        long SizeBytes,
        string ContentSha256,
        DateTimeOffset CreatedAt)
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("Id must be a non-empty Guid.", nameof(Id));
        }

        if (RunId == Guid.Empty)
        {
            throw new ArgumentException("RunId must be a non-empty Guid.", nameof(RunId));
        }

        if (string.IsNullOrWhiteSpace(Filename))
        {
            throw new ArgumentException("Filename must be non-empty.", nameof(Filename));
        }

        if (string.IsNullOrWhiteSpace(ContentType))
        {
            throw new ArgumentException("ContentType must be non-empty.", nameof(ContentType));
        }

        if (SizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SizeBytes),
                SizeBytes,
                "SizeBytes must be non-negative.");
        }

        if (string.IsNullOrEmpty(ContentSha256) || !Sha256Pattern.IsMatch(ContentSha256))
        {
            throw new ArgumentException(
                "ContentSha256 must be 64 lowercase hexadecimal characters.",
                nameof(ContentSha256));
        }

        this.Id = Id;
        this.RunId = RunId;
        this.Kind = Kind;
        this.Filename = Filename;
        this.ContentType = ContentType;
        this.SizeBytes = SizeBytes;
        this.ContentSha256 = ContentSha256;
        this.CreatedAt = CreatedAt;
    }

    public Guid Id { get; init; }
    public Guid RunId { get; init; }
    public ArtifactKind Kind { get; init; }
    public string Filename { get; init; }
    public string ContentType { get; init; }
    public long SizeBytes { get; init; }
    public string ContentSha256 { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex BuildSha256Regex();
}
