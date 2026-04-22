namespace CloudEngAgent.Infrastructure.Backends.Options;

/// <summary>
/// Options for the GitHub Models backend (https://models.inference.ai.azure.com/).
/// Binds to <see cref="SectionName"/>.
/// </summary>
public sealed record GitHubModelsOptions : BackendOptions
{
    public const string SectionName = "Backends:github-models";

    public string Model { get; init; } = string.Empty;

    /// <summary>Secret reference for the GitHub PAT used as an API key.</summary>
    public string ApiKeyRef { get; init; } = string.Empty;

    /// <summary>
    /// Optional custom inference endpoint. Defaults to
    /// <c>https://models.inference.ai.azure.com/</c> when empty.
    /// </summary>
    public string? Endpoint { get; init; }
}
