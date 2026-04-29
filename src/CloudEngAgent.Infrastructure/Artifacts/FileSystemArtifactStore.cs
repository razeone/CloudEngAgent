using System.Buffers;
using System.Security.Cryptography;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Domain.Artifacts;
using CloudEngAgent.Infrastructure.Persistence;
using CloudEngAgent.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CloudEngAgent.Infrastructure.Artifacts;

public sealed class FileSystemArtifactStore : IArtifactStore
{
    private readonly IDbContextFactory<RunsDbContext> _dbContextFactory;
    private readonly string _root;

    public FileSystemArtifactStore(
        IDbContextFactory<RunsDbContext> dbContextFactory,
        IOptions<ArtifactStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dbContextFactory);
        ArgumentNullException.ThrowIfNull(options);
        _dbContextFactory = dbContextFactory;
        var configured = options.Value.Root;
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "cloudeng-artifacts")
            : configured;
    }

    public string Root => _root;

    public async Task<Artifact> PutAsync(
        Guid runId,
        ArtifactKind kind,
        string filename,
        string contentType,
        Stream content,
        CancellationToken ct)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("runId must be a non-empty Guid.", nameof(runId));
        }

        if (string.IsNullOrWhiteSpace(filename))
        {
            throw new ArgumentException("filename must be non-empty.", nameof(filename));
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("contentType must be non-empty.", nameof(contentType));
        }

        ArgumentNullException.ThrowIfNull(content);

        var tmpDir = Path.Combine(_root, "_tmp");
        Directory.CreateDirectory(tmpDir);
        var tmpPath = Path.Combine(tmpDir, Guid.NewGuid().ToString("N") + ".tmp");

        long size;
        string sha;

        using (var sha256 = SHA256.Create())
        {
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                await using var fs = new FileStream(
                    tmpPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true);
                size = 0;
                int read;
                while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                {
                    sha256.TransformBlock(buffer, 0, read, null, 0);
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    size += read;
                }
                sha256.TransformFinalBlock([], 0, 0);
                sha = Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        var finalPath = ResolvePath(sha);
        var finalDir = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(finalDir);

        if (File.Exists(finalPath))
        {
            // Dedup hit: discard the temp.
            try { File.Delete(tmpPath); } catch { /* best effort */ }
        }
        else
        {
            try
            {
                File.Move(tmpPath, finalPath);
            }
            catch (IOException) when (File.Exists(finalPath))
            {
                // Concurrent writer beat us; clean up temp.
                try { File.Delete(tmpPath); } catch { /* best effort */ }
            }
        }

        var artifact = new Artifact(
            Guid.NewGuid(),
            runId,
            kind,
            filename,
            contentType,
            size,
            sha,
            DateTimeOffset.UtcNow);

        await using var ctx = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        ctx.Artifacts.Add(new ArtifactEntity
        {
            Id = artifact.Id,
            RunId = artifact.RunId,
            Kind = (int)artifact.Kind,
            Filename = artifact.Filename,
            ContentType = artifact.ContentType,
            SizeBytes = artifact.SizeBytes,
            ContentSha256 = artifact.ContentSha256,
            CreatedAt = artifact.CreatedAt,
        });
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        return artifact;
    }

    public async Task<Stream> OpenAsync(Guid artifactId, CancellationToken ct)
    {
        var meta = await GetMetadataAsync(artifactId, ct).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"Artifact {artifactId} not found.");

        var path = ResolvePath(meta.ContentSha256);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Artifact {artifactId} metadata exists but content file is missing at '{path}'.",
                path);
        }

        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);
    }

    public async Task<Artifact?> GetMetadataAsync(Guid artifactId, CancellationToken ct)
    {
        await using var ctx = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await ctx.Artifacts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == artifactId, ct).ConfigureAwait(false);
        return entity is null ? null : new Artifact(
            entity.Id,
            entity.RunId,
            (ArtifactKind)entity.Kind,
            entity.Filename,
            entity.ContentType,
            entity.SizeBytes,
            entity.ContentSha256,
            entity.CreatedAt);
    }

    private string ResolvePath(string sha) =>
        Path.Combine(_root, "sha256", sha[..2], sha.Substring(2, 2), sha + ".bin");
}
