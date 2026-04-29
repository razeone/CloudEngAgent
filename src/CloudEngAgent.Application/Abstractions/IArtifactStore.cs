using CloudEngAgent.Domain.Artifacts;

namespace CloudEngAgent.Application.Abstractions;

public interface IArtifactStore
{
    Task<Artifact> PutAsync(
        Guid runId,
        ArtifactKind kind,
        string filename,
        string contentType,
        Stream content,
        CancellationToken ct);

    Task<Stream> OpenAsync(Guid artifactId, CancellationToken ct);

    Task<Artifact?> GetMetadataAsync(Guid artifactId, CancellationToken ct);
}
