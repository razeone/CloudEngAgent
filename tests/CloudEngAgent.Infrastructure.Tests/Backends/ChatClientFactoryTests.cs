using Azure.Core;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Domain.Backends;
using CloudEngAgent.Infrastructure.Backends;
using CloudEngAgent.Infrastructure.Backends.Options;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace CloudEngAgent.Infrastructure.Tests.Backends;

public sealed class ChatClientFactoryTests
{
    private static ChatClientFactory BuildFactory(
        AnthropicOptions? anthropicOpts = null,
        IBackendSecretResolver? secrets = null)
    {
        var resolver = secrets ?? Substitute.For<IBackendSecretResolver>();

        return new ChatClientFactory(
            azureOpenAiOptions: Options.Create(new AzureOpenAiOptions()).AsMonitor(),
            openAiOptions: Options.Create(new OpenAiOptions()).AsMonitor(),
            gitHubModelsOptions: Options.Create(new GitHubModelsOptions()).AsMonitor(),
            azureFoundryOptions: Options.Create(new AzureFoundryOptions()).AsMonitor(),
            anthropicOptions: Options.Create(anthropicOpts ?? new AnthropicOptions
            {
                Model = "claude-3-5-sonnet-20241022",
                ApiKeyRef = "anthropic-key",
            }).AsMonitor(),
            secrets: resolver,
            credential: Substitute.For<TokenCredential>(),
            loggerFactory: NullLoggerFactory.Instance,
            serviceProvider: Substitute.For<IServiceProvider>());
    }

    [Fact]
    public void Create_Anthropic_no_longer_throws_NotImplementedException()
    {
        var resolver = Substitute.For<IBackendSecretResolver>();
        resolver.ResolveAsync("anthropic-key", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("sk-ant-fake"));

        var factory = BuildFactory(secrets: resolver);

        var act = () => factory.Create(BackendId.Anthropic);

        act.Should().NotThrow<NotImplementedException>();
    }

    [Fact]
    public void Create_Anthropic_returns_IChatClient()
    {
        var resolver = Substitute.For<IBackendSecretResolver>();
        resolver.ResolveAsync("anthropic-key", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("sk-ant-fake"));

        var factory = BuildFactory(secrets: resolver);

        var client = factory.Create(BackendId.Anthropic);

        client.Should().NotBeNull();
        client.Should().BeAssignableTo<IChatClient>();
    }

    [Fact]
    public void Create_Anthropic_returns_same_instance_on_second_call()
    {
        var resolver = Substitute.For<IBackendSecretResolver>();
        resolver.ResolveAsync("anthropic-key", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("sk-ant-fake"));

        var factory = BuildFactory(secrets: resolver);

        var first = factory.Create(BackendId.Anthropic);
        var second = factory.Create(BackendId.Anthropic);

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void Dispose_does_not_throw_when_Anthropic_client_was_created()
    {
        var resolver = Substitute.For<IBackendSecretResolver>();
        resolver.ResolveAsync("anthropic-key", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("sk-ant-fake"));

        var factory = BuildFactory(secrets: resolver);
        factory.Create(BackendId.Anthropic);

        var act = () => factory.Dispose();

        act.Should().NotThrow();
    }
}

file static class OptionsMonitorExtensions
{
    public static IOptionsMonitor<T> AsMonitor<T>(this IOptions<T> opt) where T : class
    {
        var monitor = Substitute.For<IOptionsMonitor<T>>();
        monitor.CurrentValue.Returns(opt.Value);
        return monitor;
    }
}
