namespace CloudEngAgent.Infrastructure.Backends.Options;

/// <summary>Options for the standard OpenAI backend. Binds to <see cref="SectionName"/>.</summary>
public sealed record OpenAiOptions : BackendOptions
{
    public const string SectionName = "Backends:openai";

    public string Model { get; init; } = string.Empty;

    /// <summary>Secret reference for the OpenAI API key.</summary>
    public string ApiKeyRef { get; init; } = string.Empty;
}
