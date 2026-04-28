using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Infrastructure.Backends.Adapters;
using CloudEngAgent.Infrastructure.Backends.Options;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using Xunit;

namespace CloudEngAgent.Infrastructure.Tests.Backends;

public sealed class OllamaChatClientAdapterTests
{
    private static OllamaOptions ValidOptions(string? apiKeyRef = null) => new()
    {
        Endpoint = "http://localhost:11434",
        Model = "llama3.1:8b",
        ApiKeyRef = apiKeyRef ?? string.Empty,
    };

    private static IBackendSecretResolver ResolverFor(string key)
    {
        var resolver = Substitute.For<IBackendSecretResolver>();
        resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(key));
        return resolver;
    }

    [Fact]
    public void Create_returns_non_null_IChatClient_without_ApiKeyRef()
    {
        var resolver = Substitute.For<IBackendSecretResolver>();

        var client = OllamaChatClientAdapter.Create(ValidOptions(), resolver);

        client.Should().NotBeNull();
        client.Should().BeAssignableTo<IChatClient>();
    }

    [Fact]
    public void Create_does_not_resolve_secret_when_ApiKeyRef_is_empty()
    {
        var resolver = Substitute.For<IBackendSecretResolver>();

        OllamaChatClientAdapter.Create(ValidOptions(), resolver);

        resolver.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Create_returns_non_null_IChatClient_with_ApiKeyRef()
    {
        var resolver = ResolverFor("bearer-token-abc");

        var client = OllamaChatClientAdapter.Create(
            ValidOptions(apiKeyRef: "ollama-proxy-token"),
            resolver);

        client.Should().NotBeNull();
        client.Should().BeAssignableTo<IChatClient>();
        resolver.Received(1).ResolveAsync("ollama-proxy-token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Create_disposes_without_throwing()
    {
        var client = OllamaChatClientAdapter.Create(ValidOptions(), Substitute.For<IBackendSecretResolver>());
        var act = () => client.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Create_throws_when_Endpoint_is_missing()
    {
        var opts = ValidOptions() with { Endpoint = "" };
        var act = () => OllamaChatClientAdapter.Create(opts, Substitute.For<IBackendSecretResolver>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Endpoint*");
    }

    [Fact]
    public void Create_throws_when_Model_is_missing()
    {
        var opts = ValidOptions() with { Model = "" };
        var act = () => OllamaChatClientAdapter.Create(opts, Substitute.For<IBackendSecretResolver>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Model*");
    }

    [Fact]
    public void Create_propagates_exception_when_secret_resolver_fails()
    {
        var resolver = Substitute.For<IBackendSecretResolver>();
        resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("Secret 'ollama-proxy-token' not found."));

        var act = () => OllamaChatClientAdapter.Create(
            ValidOptions(apiKeyRef: "ollama-proxy-token"),
            resolver);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ollama-proxy-token*");
    }
}
