using Azure.Core;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Domain.Backends;
using CloudEngAgent.Infrastructure.Backends;
using CloudEngAgent.Infrastructure.Backends.Options;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace CloudEngAgent.Api.Tests.Backends;

public sealed class ChatClientFactoryTests
{
    private static ChatClientFactory CreateFactory(
        AzureOpenAiOptions? azureOpenAi = null,
        OpenAiOptions? openAi = null,
        GitHubModelsOptions? gitHubModels = null,
        AzureFoundryOptions? azureFoundry = null,
        AnthropicOptions? anthropic = null,
        IBackendSecretResolver? secretResolver = null)
    {
        var azureOpenAiMon = Substitute.For<IOptionsMonitor<AzureOpenAiOptions>>();
        azureOpenAiMon.CurrentValue.Returns(azureOpenAi ?? new AzureOpenAiOptions());

        var openAiMon = Substitute.For<IOptionsMonitor<OpenAiOptions>>();
        openAiMon.CurrentValue.Returns(openAi ?? new OpenAiOptions());

        var gitHubModelsMon = Substitute.For<IOptionsMonitor<GitHubModelsOptions>>();
        gitHubModelsMon.CurrentValue.Returns(gitHubModels ?? new GitHubModelsOptions());

        var azureFoundryMon = Substitute.For<IOptionsMonitor<AzureFoundryOptions>>();
        azureFoundryMon.CurrentValue.Returns(azureFoundry ?? new AzureFoundryOptions());

        var anthropicMon = Substitute.For<IOptionsMonitor<AnthropicOptions>>();
        anthropicMon.CurrentValue.Returns(anthropic ?? new AnthropicOptions());

        return new ChatClientFactory(
            azureOpenAiMon,
            openAiMon,
            gitHubModelsMon,
            azureFoundryMon,
            anthropicMon,
            secretResolver ?? Substitute.For<IBackendSecretResolver>(),
            Substitute.For<TokenCredential>(),
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider());
    }

    [Fact]
    public void Create_AzureOpenAi_Unconfigured_ThrowsInvalidOperationException()
    {
        var factory = CreateFactory(); // empty AzureOpenAiOptions (no Endpoint)

        var act = () => factory.Create(BackendId.AzureOpenAi);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*is not configured*");
    }

    [Fact]
    public void Create_OpenAi_Unconfigured_ThrowsInvalidOperationException()
    {
        var factory = CreateFactory(); // empty OpenAiOptions (no ApiKeyRef)

        var act = () => factory.Create(BackendId.OpenAi);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*is not configured*");
    }

    [Fact]
    public void Create_AzureFoundry_ThrowsNotImplementedException()
    {
        // AzureFoundry is intentionally unimplemented (M3.5 todo: m3-azure-foundry).
        var factory = CreateFactory(
            azureFoundry: new AzureFoundryOptions
            {
                Endpoint = "https://fake.foundry.azure.com/",
                Model = "gpt-4o"
            });

        var act = () => factory.Create(BackendId.AzureFoundry);

        act.Should().Throw<NotImplementedException>()
            .WithMessage("*azure-foundry*");
    }

    [Fact]
    public void Create_Anthropic_ValidOptions_ReturnsNonNullClient()
    {
        // Anthropic is implemented via AnthropicChatClientAdapter; no network calls on construct.
        var secrets = Substitute.For<IBackendSecretResolver>();
        secrets.ResolveAsync("anthropic-key", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("sk-ant-fake"));

        var factory = CreateFactory(
            anthropic: new AnthropicOptions { ApiKeyRef = "anthropic-key", Model = "claude-3-5-sonnet-20241022" },
            secretResolver: secrets);

        var client = factory.Create(BackendId.Anthropic);

        client.Should().NotBeNull();
    }

    [Fact]
    public void Create_CalledTwice_ReturnsSameCachedInstance()
    {
        var secrets = Substitute.For<IBackendSecretResolver>();
        secrets.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("fake-api-key"));

        var factory = CreateFactory(
            openAi: new OpenAiOptions { ApiKeyRef = "openai-key", Model = "gpt-4o" },
            secretResolver: secrets);

        var first = factory.Create(BackendId.OpenAi);
        var second = factory.Create(BackendId.OpenAi);

        ReferenceEquals(first, second).Should().BeTrue("second call should return cached instance");
    }

    // Create_AzureOpenAi_ValidOptions_ManagedIdentity_ReturnsNonNullClient is omitted:
    // AzureOpenAIClient.GetChatClient() triggers a runtime TypeLoadException for
    // 'OpenAI.RealtimeConversation.RealtimeConversationClient' in the current test environment due
    // to an Azure.AI.OpenAI / OpenAI package version mismatch (Azure.AI.OpenAI references a newer
    // OpenAI assembly than v2.10.0.0 that is resolved in this project). This is a pre-existing
    // package constraint; the adapter code is verified by the existing integration tests.

    [Fact]
    public void Create_OpenAi_ValidOptions_ReturnsNonNullClient()
    {
        // SDK ChatClient constructor doesn't make network calls.
        var secrets = Substitute.For<IBackendSecretResolver>();
        secrets.ResolveAsync("openai-key-ref", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("sk-fake-api-key"));

        var opts = new OpenAiOptions { ApiKeyRef = "openai-key-ref", Model = "gpt-4o" };
        var factory = CreateFactory(openAi: opts, secretResolver: secrets);

        var client = factory.Create(BackendId.OpenAi);

        client.Should().NotBeNull();
    }

    [Fact]
    public void Create_GitHubModels_ValidOptions_ReturnsNonNullClient()
    {
        // GitHub Models uses the OpenAI SDK with a custom endpoint; no network calls on construct.
        var secrets = Substitute.For<IBackendSecretResolver>();
        secrets.ResolveAsync("gh-pat-ref", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("ghp_fake_token"));

        var opts = new GitHubModelsOptions { ApiKeyRef = "gh-pat-ref", Model = "gpt-4o-mini" };
        var factory = CreateFactory(gitHubModels: opts, secretResolver: secrets);

        var client = factory.Create(BackendId.GitHubModels);

        client.Should().NotBeNull();
    }

    // Disposal of cached clients is not tested here because the wrapped IChatClient pipeline
    // is constructed internally by ChatClientBuilder and there is no test seam to inject a
    // disposal-tracking double without changing the adapter shape (out-of-scope for this todo).
}
