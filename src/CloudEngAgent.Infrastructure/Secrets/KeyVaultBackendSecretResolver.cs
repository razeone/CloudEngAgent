using System.Collections.Concurrent;
using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using CloudEngAgent.Application.Abstractions;

namespace CloudEngAgent.Infrastructure.Secrets;

/// <summary>
/// Resolves secrets from Azure Key Vault using <see cref="DefaultAzureCredential"/>.
/// Results are cached for the lifetime of the process.
/// Falls back to a chained <see cref="ConfigurationBackendSecretResolver"/>
/// when a secret is not found in Key Vault (HTTP 404), enabling local dev via user-secrets.
/// </summary>
internal sealed class KeyVaultBackendSecretResolver : IBackendSecretResolver
{
    private readonly SecretClient _secretClient;
    private readonly ConfigurationBackendSecretResolver _fallback;
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

    public KeyVaultBackendSecretResolver(
        Uri vaultUri,
        TokenCredential credential,
        ConfigurationBackendSecretResolver fallback)
    {
        _secretClient = new SecretClient(vaultUri, credential);
        _fallback = fallback;
    }

    /// <summary>Internal constructor for unit testing — accepts a pre-built SecretClient seam.</summary>
    internal KeyVaultBackendSecretResolver(
        SecretClient secretClient,
        ConfigurationBackendSecretResolver fallback)
    {
        _secretClient = secretClient;
        _fallback = fallback;
    }

    public async Task<string> ResolveAsync(string secretRef, CancellationToken ct)
    {
        if (_cache.TryGetValue(secretRef, out var cached))
            return cached;

        try
        {
            var response = await _secretClient.GetSecretAsync(secretRef, version: null, ct)
                .ConfigureAwait(false);
            var value = response.Value.Value;
            _cache[secretRef] = value;
            return value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Secret not in Key Vault — fall back to configuration / environment variables.
            return await _fallback.ResolveAsync(secretRef, ct).ConfigureAwait(false);
        }
    }
}
