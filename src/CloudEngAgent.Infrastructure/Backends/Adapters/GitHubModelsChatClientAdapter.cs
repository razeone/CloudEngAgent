using System.ClientModel;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Infrastructure.Backends.Options;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace CloudEngAgent.Infrastructure.Backends.Adapters;

/// <summary>
/// Static factory for creating an <see cref="IChatClient"/> backed by GitHub Models
/// (https://models.inference.ai.azure.com/), which uses the OpenAI SDK with a custom endpoint.
/// The API key is the user's GitHub PAT.
/// </summary>
internal static class GitHubModelsChatClientAdapter
{
    private const string DefaultEndpoint = "https://models.inference.ai.azure.com/";

    public static IChatClient Create(GitHubModelsOptions opts, IBackendSecretResolver secrets)
    {
        if (string.IsNullOrEmpty(opts.ApiKeyRef))
            throw new InvalidOperationException(
                "Backend 'github-models' is not configured: missing 'ApiKeyRef'.");

        if (string.IsNullOrEmpty(opts.Model))
            throw new InvalidOperationException(
                "Backend 'github-models' is not configured: missing 'Model'.");

        var apiKey = secrets.ResolveAsync(opts.ApiKeyRef, CancellationToken.None)
            .GetAwaiter().GetResult();

        var endpointUri = new Uri(
            string.IsNullOrEmpty(opts.Endpoint) ? DefaultEndpoint : opts.Endpoint);

        var clientOptions = new OpenAIClientOptions { Endpoint = endpointUri };
        var credential = new ApiKeyCredential(apiKey);

        return new ChatClient(opts.Model, credential, clientOptions).AsIChatClient();
    }
}
