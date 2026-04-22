using CloudEngAgent.Infrastructure.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CloudEngAgent.Api.Tests.Backends;

public sealed class ConfigurationBackendSecretResolverTests
{
    private static IConfiguration BuildConfig(params (string key, string value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.key, e.value)))
            .Build();

    [Fact]
    public async Task ResolveAsync_ReturnsValue_FromConfiguration()
    {
        var config = BuildConfig(("Secrets:my-secret", "config-value"));
        var resolver = new ConfigurationBackendSecretResolver(config);

        var result = await resolver.ResolveAsync("my-secret", CancellationToken.None);

        result.Should().Be("config-value");
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToEnvVar_WhenNotInConfig()
    {
        // "cloudeng-test-fallback" → env var "CLOUDENG_TEST_FALLBACK"
        const string SecretRef = "cloudeng-test-fallback-xunit";
        const string EnvVarName = "CLOUDENG_TEST_FALLBACK_XUNIT";

        Environment.SetEnvironmentVariable(EnvVarName, "env-value");
        try
        {
            var config = BuildConfig(); // no Secrets entry
            var resolver = new ConfigurationBackendSecretResolver(config);

            var result = await resolver.ResolveAsync(SecretRef, CancellationToken.None);

            result.Should().Be("env-value");
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, null);
        }
    }

    [Fact]
    public async Task ResolveAsync_Throws_WhenNeitherConfigNorEnvVarResolves()
    {
        const string SecretRef = "nonexistent-secret-xyz987";

        var config = BuildConfig(); // empty
        Environment.SetEnvironmentVariable("NONEXISTENT_SECRET_XYZ987", null);
        var resolver = new ConfigurationBackendSecretResolver(config);

        var act = async () => await resolver.ResolveAsync(SecretRef, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{SecretRef}*")
            .WithMessage("*Secrets:nonexistent-secret-xyz987*")
            .WithMessage("*NONEXISTENT_SECRET_XYZ987*");
    }

    [Fact]
    public async Task ResolveAsync_PreservesValueCaseSensitivity()
    {
        const string ExpectedValue = "MyMixedCaseValue_123";
        var config = BuildConfig(("Secrets:case-test", ExpectedValue));
        var resolver = new ConfigurationBackendSecretResolver(config);

        var result = await resolver.ResolveAsync("case-test", CancellationToken.None);

        result.Should().Be(ExpectedValue);
    }

    [Fact]
    public async Task ResolveAsync_ConfigTakesPrecedence_OverEnvVar()
    {
        const string SecretRef = "cloudeng-test-precedence-xunit";
        const string EnvVarName = "CLOUDENG_TEST_PRECEDENCE_XUNIT";

        Environment.SetEnvironmentVariable(EnvVarName, "env-value-should-not-win");
        try
        {
            var config = BuildConfig(($"Secrets:{SecretRef}", "config-wins"));
            var resolver = new ConfigurationBackendSecretResolver(config);

            var result = await resolver.ResolveAsync(SecretRef, CancellationToken.None);

            result.Should().Be("config-wins");
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, null);
        }
    }
}
