using System.Net.Http.Headers;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Infrastructure.Backends.Options;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace CloudEngAgent.Infrastructure.Backends.Adapters;

/// <summary>
/// Static factory for creating an <see cref="IChatClient"/> backed by an Ollama server.
/// <para>
/// <see cref="OllamaApiClient"/> from OllamaSharp implements <see cref="IChatClient"/>
/// natively. When <see cref="OllamaOptions.ApiKeyRef"/> is set, requests are sent through
/// an <see cref="HttpClient"/> with a <c>Bearer</c> <see cref="AuthenticationHeaderValue"/>
/// for proxied/secured deployments; otherwise the server is contacted unauthenticated.
/// </para>
/// </summary>
internal static class OllamaChatClientAdapter
{
    public static IChatClient Create(OllamaOptions opts, IBackendSecretResolver secrets)
    {
        if (string.IsNullOrEmpty(opts.Endpoint))
            throw new InvalidOperationException(
                "Backend 'ollama' is not configured: missing 'Endpoint'.");

        if (string.IsNullOrEmpty(opts.Model))
            throw new InvalidOperationException(
                "Backend 'ollama' is not configured: missing 'Model'.");

        var endpoint = new Uri(opts.Endpoint);

        if (string.IsNullOrEmpty(opts.ApiKeyRef))
        {
            return new OllamaApiClient(endpoint, opts.Model);
        }

        var apiKey = secrets.ResolveAsync(opts.ApiKeyRef, CancellationToken.None)
            .GetAwaiter().GetResult();

        var http = new HttpClient { BaseAddress = endpoint };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return new OllamaApiClient(http, opts.Model);
    }
}
