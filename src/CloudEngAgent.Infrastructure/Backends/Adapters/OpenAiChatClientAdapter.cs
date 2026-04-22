using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Infrastructure.Backends.Options;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace CloudEngAgent.Infrastructure.Backends.Adapters;

/// <summary>
/// Static factory for creating an <see cref="IChatClient"/> backed by the standard OpenAI API.
/// </summary>
internal static class OpenAiChatClientAdapter
{
    public static IChatClient Create(OpenAiOptions opts, IBackendSecretResolver secrets)
    {
        if (string.IsNullOrEmpty(opts.ApiKeyRef))
            throw new InvalidOperationException(
                "Backend 'openai' is not configured: missing 'ApiKeyRef'.");

        if (string.IsNullOrEmpty(opts.Model))
            throw new InvalidOperationException(
                "Backend 'openai' is not configured: missing 'Model'.");

        var apiKey = secrets.ResolveAsync(opts.ApiKeyRef, CancellationToken.None)
            .GetAwaiter().GetResult();

        return new ChatClient(opts.Model, apiKey).AsIChatClient();
    }
}
