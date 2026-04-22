using Azure;
using Azure.AI.OpenAI;
using Azure.Core;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Infrastructure.Backends.Options;
using Microsoft.Extensions.AI;

namespace CloudEngAgent.Infrastructure.Backends.Adapters;

/// <summary>
/// Static factory for creating an <see cref="IChatClient"/> backed by Azure OpenAI.
/// Supports both Managed Identity and API-key authentication modes.
/// </summary>
internal static class AzureOpenAiChatClientAdapter
{
    public static IChatClient Create(
        AzureOpenAiOptions opts,
        IBackendSecretResolver secrets,
        TokenCredential credential)
    {
        if (string.IsNullOrEmpty(opts.Endpoint))
            throw new InvalidOperationException(
                "Backend 'azure-openai' is not configured: missing 'Endpoint'.");

        if (string.IsNullOrEmpty(opts.Deployment))
            throw new InvalidOperationException(
                "Backend 'azure-openai' is not configured: missing 'Deployment'.");

        var endpoint = new Uri(opts.Endpoint);
        AzureOpenAIClient azureClient;

        if (string.Equals(opts.AuthMode, "ApiKey", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(opts.ApiKeyRef))
                throw new InvalidOperationException(
                    "Backend 'azure-openai' is not configured: 'ApiKeyRef' is required when AuthMode is 'ApiKey'.");

            var apiKey = secrets.ResolveAsync(opts.ApiKeyRef, CancellationToken.None)
                .GetAwaiter().GetResult();

            azureClient = new AzureOpenAIClient(endpoint, new AzureKeyCredential(apiKey));
        }
        else
        {
            azureClient = new AzureOpenAIClient(endpoint, credential);
        }

        return azureClient.GetChatClient(opts.Deployment).AsIChatClient();
    }
}
