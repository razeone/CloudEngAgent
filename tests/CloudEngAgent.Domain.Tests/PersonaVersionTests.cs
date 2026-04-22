using System.Security.Cryptography;
using System.Text;
using CloudEngAgent.Domain.Personas;
using FluentAssertions;
using Xunit;

namespace CloudEngAgent.Domain.Tests;

public class PersonaVersionTests
{
    [Fact]
    public void FromContentHash_produces_lowercase_hex()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("hello"));

        var version = PersonaVersion.FromContentHash(hash);

        version.Value.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void FromContentHash_is_deterministic()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("persona-yaml-body"));

        PersonaVersion.FromContentHash(hash).Should()
            .Be(PersonaVersion.FromContentHash(hash));
    }

    [Fact]
    public void FromContentHash_rejects_empty()
    {
        var act = () => PersonaVersion.FromContentHash(ReadOnlySpan<byte>.Empty);

        act.Should().Throw<ArgumentException>();
    }
}
