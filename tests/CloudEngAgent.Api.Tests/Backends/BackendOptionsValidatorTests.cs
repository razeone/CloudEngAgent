using CloudEngAgent.Infrastructure.Backends.Options;
using FluentAssertions;
using Xunit;

namespace CloudEngAgent.Api.Tests.Backends;

public sealed class BackendOptionsValidatorTests
{
    // ── AzureOpenAi ───────────────────────────────────────────────────────

    [Fact]
    public void AzureOpenAi_Empty_Passes()
    {
        var result = BackendOptionsValidator.ValidateAzureOpenAi(new AzureOpenAiOptions());
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void AzureOpenAi_EndpointOnly_Fails_WithHelpfulMessage()
    {
        var opts = new AzureOpenAiOptions { Endpoint = "https://fake.openai.azure.com/" };
        var result = BackendOptionsValidator.ValidateAzureOpenAi(opts);
        result.Succeeded.Should().BeFalse();
        result.FailureMessage.Should().Contain("Deployment");
    }

    [Fact]
    public void AzureOpenAi_FullyValid_Passes()
    {
        var opts = new AzureOpenAiOptions
        {
            Endpoint = "https://fake.openai.azure.com/",
            Deployment = "gpt-4o"
        };
        var result = BackendOptionsValidator.ValidateAzureOpenAi(opts);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void AzureOpenAi_ApiKeyMode_WithoutApiKeyRef_Fails()
    {
        var opts = new AzureOpenAiOptions
        {
            Endpoint = "https://fake.openai.azure.com/",
            Deployment = "gpt-4o",
            AuthMode = "ApiKey"
        };
        var result = BackendOptionsValidator.ValidateAzureOpenAi(opts);
        result.Succeeded.Should().BeFalse();
        result.FailureMessage.Should().Contain("ApiKeyRef");
    }

    // ── AzureFoundry ──────────────────────────────────────────────────────

    [Fact]
    public void AzureFoundry_Empty_Passes()
    {
        var result = BackendOptionsValidator.ValidateAzureFoundry(new AzureFoundryOptions());
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void AzureFoundry_EndpointOnly_Fails_WithHelpfulMessage()
    {
        var opts = new AzureFoundryOptions { Endpoint = "https://fake.foundry.azure.com/" };
        var result = BackendOptionsValidator.ValidateAzureFoundry(opts);
        result.Succeeded.Should().BeFalse();
        result.FailureMessage.Should().Contain("Model");
    }

    [Fact]
    public void AzureFoundry_FullyValid_Passes()
    {
        var opts = new AzureFoundryOptions
        {
            Endpoint = "https://fake.foundry.azure.com/",
            Model = "gpt-4o"
        };
        var result = BackendOptionsValidator.ValidateAzureFoundry(opts);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void AzureFoundry_ApiKeyMode_WithoutApiKeyRef_Fails()
    {
        var opts = new AzureFoundryOptions
        {
            Endpoint = "https://fake.foundry.azure.com/",
            Model = "gpt-4o",
            AuthMode = "ApiKey"
        };
        var result = BackendOptionsValidator.ValidateAzureFoundry(opts);
        result.Succeeded.Should().BeFalse();
        result.FailureMessage.Should().Contain("ApiKeyRef");
    }

    // ── OpenAi ────────────────────────────────────────────────────────────

    [Fact]
    public void OpenAi_Empty_Passes()
    {
        var result = BackendOptionsValidator.ValidateOpenAi(new OpenAiOptions());
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void OpenAi_ApiKeyRefOnly_Fails_WithHelpfulMessage()
    {
        var opts = new OpenAiOptions { ApiKeyRef = "my-api-key" };
        var result = BackendOptionsValidator.ValidateOpenAi(opts);
        result.Succeeded.Should().BeFalse();
        result.FailureMessage.Should().Contain("Model");
    }

    [Fact]
    public void OpenAi_FullyValid_Passes()
    {
        var opts = new OpenAiOptions { ApiKeyRef = "my-api-key", Model = "gpt-4o" };
        var result = BackendOptionsValidator.ValidateOpenAi(opts);
        result.Succeeded.Should().BeTrue();
    }

    // ── GitHubModels ──────────────────────────────────────────────────────

    [Fact]
    public void GitHubModels_Empty_Passes()
    {
        var result = BackendOptionsValidator.ValidateGitHubModels(new GitHubModelsOptions());
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void GitHubModels_ApiKeyRefOnly_Fails_WithHelpfulMessage()
    {
        var opts = new GitHubModelsOptions { ApiKeyRef = "my-pat" };
        var result = BackendOptionsValidator.ValidateGitHubModels(opts);
        result.Succeeded.Should().BeFalse();
        result.FailureMessage.Should().Contain("Model");
    }

    [Fact]
    public void GitHubModels_FullyValid_Passes()
    {
        var opts = new GitHubModelsOptions { ApiKeyRef = "my-pat", Model = "gpt-4o-mini" };
        var result = BackendOptionsValidator.ValidateGitHubModels(opts);
        result.Succeeded.Should().BeTrue();
    }

    // ── Anthropic ─────────────────────────────────────────────────────────

    [Fact]
    public void Anthropic_Empty_Passes()
    {
        var result = BackendOptionsValidator.ValidateAnthropic(new AnthropicOptions());
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Anthropic_ApiKeyRefOnly_Fails_WithHelpfulMessage()
    {
        var opts = new AnthropicOptions { ApiKeyRef = "my-api-key" };
        var result = BackendOptionsValidator.ValidateAnthropic(opts);
        result.Succeeded.Should().BeFalse();
        result.FailureMessage.Should().Contain("Model");
    }

    [Fact]
    public void Anthropic_FullyValid_Passes()
    {
        var opts = new AnthropicOptions { ApiKeyRef = "my-api-key", Model = "claude-3-5-sonnet" };
        var result = BackendOptionsValidator.ValidateAnthropic(opts);
        result.Succeeded.Should().BeTrue();
    }

    // ── Ollama ────────────────────────────────────────────────────────────

    [Fact]
    public void Ollama_Empty_Passes()
    {
        var result = BackendOptionsValidator.ValidateOllama(new OllamaOptions());
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Ollama_EndpointOnly_Fails_WithHelpfulMessage()
    {
        var opts = new OllamaOptions { Endpoint = "http://localhost:11434" };
        var result = BackendOptionsValidator.ValidateOllama(opts);
        result.Succeeded.Should().BeFalse();
        result.FailureMessage.Should().Contain("Model");
    }

    [Fact]
    public void Ollama_FullyValid_Passes()
    {
        var opts = new OllamaOptions
        {
            Endpoint = "http://localhost:11434",
            Model = "llama3.1:8b"
        };
        var result = BackendOptionsValidator.ValidateOllama(opts);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Ollama_WithApiKeyRef_Passes()
    {
        var opts = new OllamaOptions
        {
            Endpoint = "https://ollama-proxy.example.com",
            Model = "llama3.1:8b",
            ApiKeyRef = "ollama-proxy-token"
        };
        var result = BackendOptionsValidator.ValidateOllama(opts);
        result.Succeeded.Should().BeTrue();
    }
}
