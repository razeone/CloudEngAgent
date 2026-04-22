namespace CloudEngAgent.Infrastructure.Backends.Options;

/// <summary>
/// Base record for all backend configuration options.
/// </summary>
public abstract record BackendOptions
{
    /// <summary>
    /// Authentication mode. Allowed values: "ManagedIdentity" (default), "ApiKey".
    /// </summary>
    public string AuthMode { get; init; } = "ManagedIdentity";
}
