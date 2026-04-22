using Microsoft.Extensions.Options;

namespace CloudEngAgent.Infrastructure.Backends.Options;

/// <summary>
/// Shared validation logic for all backend options. Individual validators delegate here
/// to keep cross-field rules DRY.
/// </summary>
internal static class BackendOptionsValidator
{
    // ── Per-backend entry points ───────────────────────────────────────────────

    public static ValidateOptionsResult ValidateAzureOpenAi(AzureOpenAiOptions opts)
    {
        // No Endpoint → user hasn't configured this backend; skip.
        if (string.IsNullOrEmpty(opts.Endpoint))
            return ValidateOptionsResult.Success;

        var errors = new List<string>();

        if (string.IsNullOrEmpty(opts.Deployment))
            errors.Add($"'{AzureOpenAiOptions.SectionName}:Deployment' is required when Endpoint is configured.");

        ValidateAuthMode(opts.AuthMode, AzureOpenAiOptions.SectionName, errors);

        if (IsApiKeyMode(opts.AuthMode) && string.IsNullOrEmpty(opts.ApiKeyRef))
            errors.Add($"'{AzureOpenAiOptions.SectionName}:ApiKeyRef' is required when AuthMode is 'ApiKey'.");

        return Summarize(errors);
    }

    public static ValidateOptionsResult ValidateAzureFoundry(AzureFoundryOptions opts)
    {
        // No Endpoint → unconfigured; skip.
        if (string.IsNullOrEmpty(opts.Endpoint))
            return ValidateOptionsResult.Success;

        var errors = new List<string>();

        if (string.IsNullOrEmpty(opts.Model))
            errors.Add($"'{AzureFoundryOptions.SectionName}:Model' is required when Endpoint is configured.");

        ValidateAuthMode(opts.AuthMode, AzureFoundryOptions.SectionName, errors);

        if (IsApiKeyMode(opts.AuthMode) && string.IsNullOrEmpty(opts.ApiKeyRef))
            errors.Add($"'{AzureFoundryOptions.SectionName}:ApiKeyRef' is required when AuthMode is 'ApiKey'.");

        return Summarize(errors);
    }

    public static ValidateOptionsResult ValidateOpenAi(OpenAiOptions opts)
    {
        // No ApiKeyRef → user hasn't configured this backend; skip.
        if (string.IsNullOrEmpty(opts.ApiKeyRef))
            return ValidateOptionsResult.Success;

        var errors = new List<string>();

        if (string.IsNullOrEmpty(opts.Model))
            errors.Add($"'{OpenAiOptions.SectionName}:Model' is required when ApiKeyRef is configured.");

        return Summarize(errors);
    }

    public static ValidateOptionsResult ValidateGitHubModels(GitHubModelsOptions opts)
    {
        // No ApiKeyRef → user hasn't configured this backend; skip.
        if (string.IsNullOrEmpty(opts.ApiKeyRef))
            return ValidateOptionsResult.Success;

        var errors = new List<string>();

        if (string.IsNullOrEmpty(opts.Model))
            errors.Add($"'{GitHubModelsOptions.SectionName}:Model' is required when ApiKeyRef is configured.");

        return Summarize(errors);
    }

    public static ValidateOptionsResult ValidateAnthropic(AnthropicOptions opts)
    {
        // No ApiKeyRef → user hasn't configured this backend; skip.
        if (string.IsNullOrEmpty(opts.ApiKeyRef))
            return ValidateOptionsResult.Success;

        var errors = new List<string>();

        if (string.IsNullOrEmpty(opts.Model))
            errors.Add($"'{AnthropicOptions.SectionName}:Model' is required when ApiKeyRef is configured.");

        return Summarize(errors);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsApiKeyMode(string authMode) =>
        string.Equals(authMode, "ApiKey", StringComparison.OrdinalIgnoreCase);

    private static void ValidateAuthMode(string authMode, string sectionName, List<string> errors)
    {
        if (!string.IsNullOrEmpty(authMode) &&
            !string.Equals(authMode, "ManagedIdentity", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(authMode, "ApiKey", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"'{sectionName}:AuthMode' must be 'ManagedIdentity' or 'ApiKey', got '{authMode}'.");
        }
    }

    private static ValidateOptionsResult Summarize(List<string> errors) =>
        errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
}

// ── IValidateOptions<T> implementations ──────────────────────────────────────

internal sealed class AzureOpenAiOptionsValidator : IValidateOptions<AzureOpenAiOptions>
{
    public ValidateOptionsResult Validate(string? name, AzureOpenAiOptions options) =>
        BackendOptionsValidator.ValidateAzureOpenAi(options);
}

internal sealed class AzureFoundryOptionsValidator : IValidateOptions<AzureFoundryOptions>
{
    public ValidateOptionsResult Validate(string? name, AzureFoundryOptions options) =>
        BackendOptionsValidator.ValidateAzureFoundry(options);
}

internal sealed class OpenAiOptionsValidator : IValidateOptions<OpenAiOptions>
{
    public ValidateOptionsResult Validate(string? name, OpenAiOptions options) =>
        BackendOptionsValidator.ValidateOpenAi(options);
}

internal sealed class GitHubModelsOptionsValidator : IValidateOptions<GitHubModelsOptions>
{
    public ValidateOptionsResult Validate(string? name, GitHubModelsOptions options) =>
        BackendOptionsValidator.ValidateGitHubModels(options);
}

internal sealed class AnthropicOptionsValidator : IValidateOptions<AnthropicOptions>
{
    public ValidateOptionsResult Validate(string? name, AnthropicOptions options) =>
        BackendOptionsValidator.ValidateAnthropic(options);
}
