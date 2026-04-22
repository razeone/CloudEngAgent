namespace CloudEngAgent.Infrastructure.Backends.Options;

/// <summary>Options for the Anthropic backend. Binds to <see cref="SectionName"/>.</summary>
public sealed record AnthropicOptions : BackendOptions
{
    public const string SectionName = "Backends:anthropic";

    public string Model { get; init; } = string.Empty;

    /// <summary>Secret reference for the Anthropic API key.</summary>
    public string ApiKeyRef { get; init; } = string.Empty;
}
