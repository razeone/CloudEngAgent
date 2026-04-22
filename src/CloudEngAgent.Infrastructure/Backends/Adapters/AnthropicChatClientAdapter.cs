using Anthropic.SDK;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Infrastructure.Backends.Options;
using Microsoft.Extensions.AI;

namespace CloudEngAgent.Infrastructure.Backends.Adapters;

/// <summary>
/// Static factory for creating an <see cref="IChatClient"/> backed by the Anthropic API.
/// <para>
/// <see cref="Anthropic.SDK.Messaging.MessagesEndpoint"/> already implements
/// <see cref="IChatClient"/> natively; this adapter wraps it with a thin
/// <see cref="ChatClientBuilder"/> pipeline that injects the configured default
/// <see cref="ChatOptions.ModelId"/> so callers that omit it still hit the right model.
/// </para>
/// </summary>
internal static class AnthropicChatClientAdapter
{
    public static IChatClient Create(AnthropicOptions opts, IBackendSecretResolver secrets)
    {
        if (string.IsNullOrEmpty(opts.ApiKeyRef))
            throw new InvalidOperationException(
                "Backend 'anthropic' is not configured: missing 'ApiKeyRef'.");

        if (string.IsNullOrEmpty(opts.Model))
            throw new InvalidOperationException(
                "Backend 'anthropic' is not configured: missing 'Model'.");

        var apiKey = secrets.ResolveAsync(opts.ApiKeyRef, CancellationToken.None)
            .GetAwaiter().GetResult();

        var client = new AnthropicClient(new APIAuthentication(apiKey));

        // MessagesEndpoint already implements IChatClient (Anthropic.SDK 5.2.0).
        // Wrap with a ConfigureOptions step so the model from config is used as
        // the default when the caller does not supply ChatOptions.ModelId.
        return new ChatClientBuilder(client.Messages)
            .ConfigureOptions(o => o.ModelId ??= opts.Model)
            .Build();
    }
}
