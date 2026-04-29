using System.Text;
using CloudEngAgent.Domain.Artifacts;
using CloudEngAgent.Domain.Runs;
using CloudEngAgent.Infrastructure.Artifacts;
using CloudEngAgent.Infrastructure.Persistence;
using CloudEngAgent.Infrastructure.Persistence.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace CloudEngAgent.Infrastructure.Tests.Artifacts;

[Collection("MsSql")]
public sealed class FileSystemArtifactStoreTests : IDisposable
{
    private readonly MsSqlContainerFixture _fixture;
    private readonly string _root;

    public FileSystemArtifactStoreTests(MsSqlContainerFixture fixture)
    {
        _fixture = fixture;
        _root = Path.Combine(
            Path.GetTempPath(),
            "cloudeng-artifacts-tests",
            Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    private FileSystemArtifactStore CreateStore() =>
        new(
            _fixture.DbContextFactory ?? throw new InvalidOperationException("Fixture not ready."),
            Options.Create(new ArtifactStoreOptions { Root = _root }));

    private async Task<Guid> SeedRunAsync()
    {
        var runId = Guid.NewGuid();
        await using var ctx = _fixture.DbContextFactory!.CreateDbContext();
        ctx.Runs.Add(new RunEntity
        {
            Id = runId,
            WorkflowId = "test-workflow",
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            EndedAt = null,
            InputSummary = "test",
        });
        await ctx.SaveChangesAsync();
        return runId;
    }

    [Fact]
    public async Task PutAsync_then_OpenAsync_round_trips_content()
    {
        if (_fixture.SkipReason is not null) return;

        var store = CreateStore();
        var runId = await SeedRunAsync();
        var bytes = Encoding.UTF8.GetBytes("hello, artifact world");

        Artifact saved;
        using (var ms = new MemoryStream(bytes))
        {
            saved = await store.PutAsync(
                runId,
                ArtifactKind.MarkdownReport,
                "report.md",
                "text/markdown",
                ms,
                CancellationToken.None);
        }

        saved.RunId.Should().Be(runId);
        saved.SizeBytes.Should().Be(bytes.LongLength);
        saved.ContentSha256.Should().MatchRegex("^[0-9a-f]{64}$");

        var meta = await store.GetMetadataAsync(saved.Id, CancellationToken.None);
        meta.Should().NotBeNull();
        meta!.ContentSha256.Should().Be(saved.ContentSha256);

        await using var read = await store.OpenAsync(saved.Id, CancellationToken.None);
        using var reader = new StreamReader(read);
        var actual = await reader.ReadToEndAsync();
        actual.Should().Be("hello, artifact world");
    }

    [Fact]
    public async Task PutAsync_with_identical_content_dedups_on_disk()
    {
        if (_fixture.SkipReason is not null) return;

        var store = CreateStore();
        var runId = await SeedRunAsync();
        var bytes = Encoding.UTF8.GetBytes("same bytes");

        Artifact a1, a2;
        using (var s1 = new MemoryStream(bytes))
        {
            a1 = await store.PutAsync(runId, ArtifactKind.Other, "a.bin", "application/octet-stream", s1, CancellationToken.None);
        }
        using (var s2 = new MemoryStream(bytes))
        {
            a2 = await store.PutAsync(runId, ArtifactKind.Other, "b.bin", "application/octet-stream", s2, CancellationToken.None);
        }

        a1.Id.Should().NotBe(a2.Id);
        a1.ContentSha256.Should().Be(a2.ContentSha256);

        var sha = a1.ContentSha256;
        var path = Path.Combine(_root, "sha256", sha[..2], sha.Substring(2, 2), sha + ".bin");
        File.Exists(path).Should().BeTrue();

        var shaDir = Path.Combine(_root, "sha256");
        Directory.EnumerateFiles(shaDir, sha + ".bin", SearchOption.AllDirectories)
            .Should().ContainSingle("identical content must not be duplicated on disk");

        var tmpDir = Path.Combine(_root, "_tmp");
        if (Directory.Exists(tmpDir))
        {
            Directory.EnumerateFiles(tmpDir).Should().BeEmpty("temp files must be cleaned up");
        }
    }

    [Fact]
    public async Task PutAsync_with_different_content_creates_distinct_blobs()
    {
        if (_fixture.SkipReason is not null) return;

        var store = CreateStore();
        var runId = await SeedRunAsync();

        Artifact a1, a2;
        using (var s1 = new MemoryStream(Encoding.UTF8.GetBytes("alpha")))
        {
            a1 = await store.PutAsync(runId, ArtifactKind.Other, "a.bin", "application/octet-stream", s1, CancellationToken.None);
        }
        using (var s2 = new MemoryStream(Encoding.UTF8.GetBytes("beta")))
        {
            a2 = await store.PutAsync(runId, ArtifactKind.Other, "b.bin", "application/octet-stream", s2, CancellationToken.None);
        }

        a1.ContentSha256.Should().NotBe(a2.ContentSha256);
    }
}
