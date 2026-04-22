namespace CloudEngAgent.Infrastructure.Backends.Options;

/// <summary>Options for the Azure AI Foundry backend. Binds to <see cref="SectionName"/>.</summary>
public sealed record AzureFoundryOptions : BackendOptions
{
    public const string SectionName = "Backends:azure-foundry";

    public string Endpoint { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;

    /// <summary>Secret reference used when <see cref="BackendOptions.AuthMode"/> is "ApiKey".</summary>
    public string? ApiKeyRef { get; init; }
}
