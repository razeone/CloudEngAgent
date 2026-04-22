using Azure.Core;
using Azure.Identity;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Application.Runs;
using CloudEngAgent.Infrastructure.Backends;
using CloudEngAgent.Infrastructure.Backends.Options;
using CloudEngAgent.Infrastructure.Persistence;
using CloudEngAgent.Infrastructure.Personas;
using CloudEngAgent.Infrastructure.Runs;
using CloudEngAgent.Infrastructure.Secrets;
using CloudEngAgent.Infrastructure.Sse;
using CloudEngAgent.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloudEngAgent.Infrastructure;

/// <summary>
/// Composition root for the Infrastructure layer. Registers the in-memory
/// stubs that satisfy the Application abstractions for the API milestone.
/// Real EF Core / LLM / MCP implementations replace these registrations in
/// later plans without changing the API-layer code.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? hostEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IClock, SystemClock>();

        var cs = configuration.GetConnectionString("Runs");
        if (!string.IsNullOrEmpty(cs))
        {
            services.AddPooledDbContextFactory<RunsDbContext>(opts => opts.UseSqlServer(cs));
            services.AddSingleton<IRunStore, EfCoreRunStore>();
            services.AddHealthChecks().AddDbContextCheck<RunsDbContext>("runs-db");
        }
        else if (hostEnvironment is null || hostEnvironment.IsDevelopment())
        {
            services.AddHealthChecks();
            services.AddSingleton<InMemoryRunStore>();
            services.AddSingleton<IRunStore>(sp =>
            {
                sp.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("CloudEngAgent.Infrastructure.InMemoryRunStore")
                    .LogWarning(
                        "ConnectionStrings:Runs is not configured; using InMemoryRunStore " +
                        "(Development only). Data will not be persisted.");
                return sp.GetRequiredService<InMemoryRunStore>();
            });
        }
        else
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Runs is required outside Development.");
        }

        services.AddSingleton<IRunEventBus, InMemoryRunEventBus>();

        services.AddSingleton<IPersonaRepository, InMemoryPersonaRepository>();
        services.AddSingleton<IWorkflowRegistry, InMemoryWorkflowRegistry>();
        services.AddSingleton<IWorkflowEngine, StubWorkflowEngine>();

        // ── Backend options ────────────────────────────────────────────────────
        services.AddOptions<AzureOpenAiOptions>()
            .Bind(configuration.GetSection(AzureOpenAiOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AzureOpenAiOptions>, AzureOpenAiOptionsValidator>();

        services.AddOptions<AzureFoundryOptions>()
            .Bind(configuration.GetSection(AzureFoundryOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AzureFoundryOptions>, AzureFoundryOptionsValidator>();

        services.AddOptions<OpenAiOptions>()
            .Bind(configuration.GetSection(OpenAiOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<OpenAiOptions>, OpenAiOptionsValidator>();

        services.AddOptions<GitHubModelsOptions>()
            .Bind(configuration.GetSection(GitHubModelsOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<GitHubModelsOptions>, GitHubModelsOptionsValidator>();

        services.AddOptions<AnthropicOptions>()
            .Bind(configuration.GetSection(AnthropicOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AnthropicOptions>, AnthropicOptionsValidator>();

        // ── Secrets ────────────────────────────────────────────────────────────
        var kvUri = configuration["KeyVault:Uri"];
        if (!string.IsNullOrEmpty(kvUri))
        {
            services.AddSingleton<ConfigurationBackendSecretResolver>();
            services.AddSingleton<IBackendSecretResolver>(sp =>
                new KeyVaultBackendSecretResolver(
                    new Uri(kvUri),
                    sp.GetRequiredService<TokenCredential>(),
                    sp.GetRequiredService<ConfigurationBackendSecretResolver>()));
        }
        else
        {
            services.AddSingleton<IBackendSecretResolver, ConfigurationBackendSecretResolver>();
        }

        // ── Azure credential ───────────────────────────────────────────────────
        services.TryAddSingleton<TokenCredential>(_ => new DefaultAzureCredential());

        // ── LLM chat client factory ────────────────────────────────────────────
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddSingleton<IMcpToolRegistry, EmptyMcpToolRegistry>();

        // Data Protection is required by the SSE token service. Calling AddDataProtection
        // is idempotent (TryAdd semantics inside) and gives us key rotation + ciphertext.
        services.AddDataProtection();

        var lifetimeSeconds = configuration.GetValue<int?>("Sse:TokenLifetimeSeconds") ?? 120;
        services.TryAddSingleton<ISseTokenService>(sp =>
            new DataProtectionSseTokenService(
                sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(),
                sp.GetRequiredService<IClock>(),
                TimeSpan.FromSeconds(Math.Clamp(lifetimeSeconds, 30, 600))));

        services.AddScoped<StartWorkflowRunHandler>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="InMemoryRunStore"/> as <see cref="IRunStore"/> explicitly.
    /// Intended for use in tests or local tooling that does not need a real database.
    /// </summary>
    public static IServiceCollection AddInMemoryRunStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<InMemoryRunStore>();
        services.AddSingleton<IRunStore>(sp => sp.GetRequiredService<InMemoryRunStore>());
        return services;
    }
}
