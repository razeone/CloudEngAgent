using CloudEngAgent.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace CloudEngAgent.Infrastructure.Secrets;

/// <summary>
/// Resolves secrets from <c>IConfiguration["Secrets:{secretRef}"]</c> first,
/// then falls back to an environment variable (<c>OPENAI_KEY</c> for <c>openai-key</c>).
/// </summary>
internal sealed class ConfigurationBackendSecretResolver : IBackendSecretResolver
{
    private readonly IConfiguration _configuration;

    public ConfigurationBackendSecretResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Task<string> ResolveAsync(string secretRef, CancellationToken ct)
    {
        // 1) IConfiguration["Secrets:{secretRef}"]
        var configKey = $"Secrets:{secretRef}";
        var value = _configuration[configKey];
        if (!string.IsNullOrEmpty(value))
            return Task.FromResult(value);

        // 2) Environment variable: "openai-key" → "OPENAI_KEY"
        var envVarName = secretRef.ToUpperInvariant().Replace('-', '_');
        var envValue = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrEmpty(envValue))
            return Task.FromResult(envValue);

        throw new InvalidOperationException(
            $"Secret '{secretRef}' could not be resolved. " +
            $"Tried IConfiguration['{configKey}'] and environment variable '{envVarName}'.");
    }
}
