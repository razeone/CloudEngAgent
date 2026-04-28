namespace CloudEngAgent.Infrastructure.Backends.Options;

/// <summary>
/// Options for the Ollama backend (local or proxied). Binds to <see cref="SectionName"/>.
/// </summary>
/// <remarks>
/// Ollama is unauthenticated by default. <see cref="ApiKeyRef"/> is optional and is only
/// used for proxied/secured deployments where a Bearer token must be sent on each request.
/// The inherited <see cref="BackendOptions.AuthMode"/> property is <b>not</b> honored for
/// Ollama; presence/absence of <see cref="ApiKeyRef"/> alone determines authentication.
/// </remarks>
public sealed record OllamaOptions : BackendOptions
{
    public const string SectionName = "Backends:ollama";

    /// <summary>Base URI of the Ollama server (e.g. <c>http://localhost:11434</c>).</summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>Model tag to use by default (e.g. <c>llama3.1:8b</c>).</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// Optional secret reference for a Bearer token, for Ollama deployments fronted by
    /// an authenticating proxy. Leave empty for plain unauthenticated Ollama.
    /// </summary>
    public string ApiKeyRef { get; init; } = string.Empty;
}
