using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Infrastructure.Backends.Adapters;
using CloudEngAgent.Infrastructure.Backends.Options;
using FluentAssertions;
using Microsoft.Extensions.AI;
using NSubstitute;
using Xunit;

namespace CloudEngAgent.Infrastructure.Tests.Backends;

public sealed class AnthropicChatClientAdapterTests
{
    private static AnthropicOptions ValidOptions() => new()
    {
        Model = "claude-3-5-sonnet-20241022",
        ApiKeyRef = "anthropic-key",
    };

    private static IBackendSecretResolver ResolverFor(string key)
    {
        var resolver = Substitute.For<IBackendSecretResolver>();
        resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(key));
        return resolver;
    }

    [Fact]
    public void Create_returns_non_null_IChatClient_without_network_call()
    {
        var client = AnthropicChatClientAdapter.Create(ValidOptions(), ResolverFor("sk-ant-fake"));

        client.Should().NotBeNull();
        client.Should().BeAssignableTo<IChatClient>();
    }

    [Fact]
    public void Create_disposes_without_throwing()
    {
        var client = AnthropicChatClientAdapter.Create(ValidOptions(), ResolverFor("sk-ant-fake"));
        var act = () => client.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Create_throws_when_ApiKeyRef_is_missing()
    {
        var opts = ValidOptions() with { ApiKeyRef = "" };
        var act = () => AnthropicChatClientAdapter.Create(opts, ResolverFor("sk-ant-fake"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ApiKeyRef*");
    }

    [Fact]
    public void Create_throws_when_Model_is_missing()
    {
        var opts = ValidOptions() with { Model = "" };
        var act = () => AnthropicChatClientAdapter.Create(opts, ResolverFor("sk-ant-fake"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Model*");
    }

    [Fact]
    public void Create_propagates_exception_when_secret_resolver_fails()
    {
        var resolver = Substitute.For<IBackendSecretResolver>();
        resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("Secret 'anthropic-key' not found."));

        var act = () => AnthropicChatClientAdapter.Create(ValidOptions(), resolver);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*anthropic-key*");
    }
}
