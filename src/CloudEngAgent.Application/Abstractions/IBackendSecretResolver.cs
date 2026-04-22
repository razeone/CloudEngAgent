namespace CloudEngAgent.Application.Abstractions;

/// <summary>
/// Resolves secret values for LLM backend authentication.
/// The <paramref name="secretRef"/> is typically a key vault secret name,
/// a configuration key suffix, or an environment-variable stem.
/// </summary>
public interface IBackendSecretResolver
{
    /// <summary>
    /// Resolves the value of the secret identified by <paramref name="secretRef"/>.
    /// Throws <see cref="InvalidOperationException"/> if the secret cannot be found.
    /// </summary>
    Task<string> ResolveAsync(string secretRef, CancellationToken ct);
}
