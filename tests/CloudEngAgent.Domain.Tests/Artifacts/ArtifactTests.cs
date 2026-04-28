using CloudEngAgent.Domain.Artifacts;
using FluentAssertions;
using Xunit;

namespace CloudEngAgent.Domain.Tests.Artifacts;

public class ArtifactTests
{
    private const string ValidSha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Constructor_accepts_valid_input()
    {
        var artifact = new Artifact(
            Id: Guid.NewGuid(),
            RunId: Guid.NewGuid(),
            Kind: ArtifactKind.MarkdownReport,
            Filename: "report.md",
            ContentType: "text/markdown",
            SizeBytes: 0,
            ContentSha256: ValidSha,
            CreatedAt: DateTimeOffset.UtcNow);

        artifact.ContentSha256.Should().Be(ValidSha);
        artifact.SizeBytes.Should().Be(0);
    }

    [Theory]
    [InlineData("0123")]
    [InlineData("0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("g123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("")]
    public void Constructor_throws_for_bad_sha(string sha)
    {
        var act = () => new Artifact(
            Guid.NewGuid(), Guid.NewGuid(), ArtifactKind.Pdf,
            "f.pdf", "application/pdf", 1, sha, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_throws_for_negative_size()
    {
        var act = () => new Artifact(
            Guid.NewGuid(), Guid.NewGuid(), ArtifactKind.Pdf,
            "f.pdf", "application/pdf", -1, ValidSha, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("", "application/pdf")]
    [InlineData("   ", "application/pdf")]
    [InlineData("f.pdf", "")]
    [InlineData("f.pdf", "   ")]
    public void Constructor_throws_for_empty_filename_or_content_type(string filename, string contentType)
    {
        var act = () => new Artifact(
            Guid.NewGuid(), Guid.NewGuid(), ArtifactKind.Pdf,
            filename, contentType, 1, ValidSha, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_throws_for_empty_ids()
    {
        var actId = () => new Artifact(
            Guid.Empty, Guid.NewGuid(), ArtifactKind.Pdf,
            "f.pdf", "application/pdf", 1, ValidSha, DateTimeOffset.UtcNow);
        var actRun = () => new Artifact(
            Guid.NewGuid(), Guid.Empty, ArtifactKind.Pdf,
            "f.pdf", "application/pdf", 1, ValidSha, DateTimeOffset.UtcNow);

        actId.Should().Throw<ArgumentException>();
        actRun.Should().Throw<ArgumentException>();
    }
}
