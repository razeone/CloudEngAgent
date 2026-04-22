using Azure;
using Azure.Security.KeyVault.Secrets;
using CloudEngAgent.Infrastructure.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace CloudEngAgent.Api.Tests.Backends;

/// <summary>
/// Tests for <see cref="KeyVaultBackendSecretResolver"/>.
/// <para>
/// <see cref="SecretClient"/> is non-sealed with virtual methods (Azure SDK design for testability),
/// so NSubstitute can substitute it. The internal test constructor on
/// <see cref="KeyVaultBackendSecretResolver"/> accepts a pre-built <see cref="SecretClient"/>
/// to inject the substitute without making network calls.
/// </para>
/// </summary>
public sealed class KeyVaultBackendSecretResolverTests
{
    private static ConfigurationBackendSecretResolver CreateFallback(
        params (string key, string value)[] entries)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.key, e.value)))
            .Build();
        return new ConfigurationBackendSecretResolver(config);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsSecretValue_FromKeyVault()
    {
        var fakeClient = Substitute.For<SecretClient>();
        var secret = new KeyVaultSecret("my-ref", "kv-value");
        var mockResponse = Substitute.For<Response<KeyVaultSecret>>();
        mockResponse.Value.Returns(secret);
        fakeClient.GetSecretAsync("my-ref", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(mockResponse));

        var resolver = new KeyVaultBackendSecretResolver(fakeClient, CreateFallback());

        var result = await resolver.ResolveAsync("my-ref", CancellationToken.None);

        result.Should().Be("kv-value");
    }

    [Fact]
    public async Task ResolveAsync_CachesResult_SecondCallDoesNotHitClient()
    {
        var fakeClient = Substitute.For<SecretClient>();
        var secret = new KeyVaultSecret("cached-ref", "cached-value");
        var mockResponse = Substitute.For<Response<KeyVaultSecret>>();
        mockResponse.Value.Returns(secret);
        fakeClient.GetSecretAsync("cached-ref", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(mockResponse));

        var resolver = new KeyVaultBackendSecretResolver(fakeClient, CreateFallback());

        await resolver.ResolveAsync("cached-ref", CancellationToken.None);
        await resolver.ResolveAsync("cached-ref", CancellationToken.None);

        // Client should have been called exactly once; second call served from cache.
        await fakeClient.Received(1).GetSecretAsync("cached-ref", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_404_FallsBackToConfigurationResolver()
    {
        var fakeClient = Substitute.For<SecretClient>();
        fakeClient.GetSecretAsync("missing-ref", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Response<KeyVaultSecret>>(
                new RequestFailedException(404, "Secret not found")));

        var fallback = CreateFallback(("Secrets:missing-ref", "fallback-value"));
        var resolver = new KeyVaultBackendSecretResolver(fakeClient, fallback);

        var result = await resolver.ResolveAsync("missing-ref", CancellationToken.None);

        result.Should().Be("fallback-value");
    }
}
