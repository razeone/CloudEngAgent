namespace CloudEngAgent.Infrastructure.Backends.Options;

/// <summary>Options for the Azure OpenAI backend. Binds to <see cref="SectionName"/>.</summary>
public sealed record AzureOpenAiOptions : BackendOptions
{
    public const string SectionName = "Backends:azure-openai";

    public string Endpoint { get; init; } = string.Empty;
    public string Deployment { get; init; } = string.Empty;

    /// <summary>Secret reference used when <see cref="BackendOptions.AuthMode"/> is "ApiKey".</summary>
    public string? ApiKeyRef { get; init; }
}
