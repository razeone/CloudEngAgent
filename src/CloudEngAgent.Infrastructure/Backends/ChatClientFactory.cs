using System.Collections.Concurrent;
using Azure.Core;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Domain.Backends;
using CloudEngAgent.Infrastructure.Backends.Adapters;
using CloudEngAgent.Infrastructure.Backends.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloudEngAgent.Infrastructure.Backends;

/// <summary>
/// Production <see cref="IChatClientFactory"/> that builds real LLM backends on first use
/// and caches the resulting <see cref="IChatClient"/> for the process lifetime.
/// </summary>
public sealed class ChatClientFactory : IChatClientFactory, IDisposable
{
    private readonly IOptionsMonitor<AzureOpenAiOptions> _azureOpenAiOptions;
    private readonly IOptionsMonitor<OpenAiOptions> _openAiOptions;
    private readonly IOptionsMonitor<GitHubModelsOptions> _gitHubModelsOptions;
    private readonly IOptionsMonitor<AzureFoundryOptions> _azureFoundryOptions;
    private readonly IOptionsMonitor<AnthropicOptions> _anthropicOptions;
    private readonly IOptionsMonitor<OllamaOptions> _ollamaOptions;
    private readonly IBackendSecretResolver _secrets;
    private readonly TokenCredential _credential;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, IChatClient> _cache = new(StringComparer.Ordinal);

    public ChatClientFactory(
        IOptionsMonitor<AzureOpenAiOptions> azureOpenAiOptions,
        IOptionsMonitor<OpenAiOptions> openAiOptions,
        IOptionsMonitor<GitHubModelsOptions> gitHubModelsOptions,
        IOptionsMonitor<AzureFoundryOptions> azureFoundryOptions,
        IOptionsMonitor<AnthropicOptions> anthropicOptions,
        IOptionsMonitor<OllamaOptions> ollamaOptions,
        IBackendSecretResolver secrets,
        TokenCredential credential,
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider)
    {
        _azureOpenAiOptions = azureOpenAiOptions;
        _openAiOptions = openAiOptions;
        _gitHubModelsOptions = gitHubModelsOptions;
        _azureFoundryOptions = azureFoundryOptions;
        _anthropicOptions = anthropicOptions;
        _ollamaOptions = ollamaOptions;
        _secrets = secrets;
        _credential = credential;
        _loggerFactory = loggerFactory;
        _serviceProvider = serviceProvider;
    }

    public IChatClient Create(BackendId backend) =>
        _cache.GetOrAdd(backend.Value, _ => CreateCore(backend));

    private IChatClient CreateCore(BackendId backend)
    {
        IChatClient raw;

        if (backend == BackendId.AzureOpenAi)
        {
            raw = AzureOpenAiChatClientAdapter.Create(
                _azureOpenAiOptions.CurrentValue, _secrets, _credential);
        }
        else if (backend == BackendId.OpenAi)
        {
            raw = OpenAiChatClientAdapter.Create(_openAiOptions.CurrentValue, _secrets);
        }
        else if (backend == BackendId.GitHubModels)
        {
            raw = GitHubModelsChatClientAdapter.Create(_gitHubModelsOptions.CurrentValue, _secrets);
        }
        else if (backend == BackendId.AzureFoundry)
        {
            throw new NotImplementedException(
                "Backend 'azure-foundry' is not yet implemented. See the M3.5 todo (m3-azure-foundry).");
        }
        else if (backend == BackendId.Anthropic)
        {
            raw = AnthropicChatClientAdapter.Create(_anthropicOptions.CurrentValue, _secrets);
        }
        else if (backend == BackendId.Ollama)
        {
            raw = OllamaChatClientAdapter.Create(_ollamaOptions.CurrentValue, _secrets);
        }
        else
        {
            throw new InvalidOperationException($"Unknown backend '{backend}'.");
        }

        return new ChatClientBuilder(raw)
            .UseFunctionInvocation(_loggerFactory)
            .UseLogging(_loggerFactory)
            .Build(_serviceProvider);
    }

    public void Dispose()
    {
        foreach (var client in _cache.Values)
        {
            client.Dispose();
        }

        _cache.Clear();
    }
}
