using Azure.Core;
using Azure.Identity;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Application.Runs;
using CloudEngAgent.Infrastructure.Artifacts;
using CloudEngAgent.Infrastructure.Backends;
using CloudEngAgent.Infrastructure.Backends.Options;
using CloudEngAgent.Infrastructure.Inputs;
using CloudEngAgent.Infrastructure.Mcp;
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

            services.AddOptions<ArtifactStoreOptions>()
                .Bind(configuration.GetSection(ArtifactStoreOptions.SectionName));
            services.AddScoped<IInputRequestStore, InputRequestStore>();
            services.AddScoped<IArtifactStore, FileSystemArtifactStore>();
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

        RegisterPersonaRepository(services, configuration, hostEnvironment);
        services.AddSingleton<IWorkflowRegistry, InMemoryWorkflowRegistry>();
        RegisterWorkflowEngine(services, configuration, hostEnvironment);

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

        services.AddOptions<OllamaOptions>()
            .Bind(configuration.GetSection(OllamaOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<OllamaOptions>, OllamaOptionsValidator>();

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
        RegisterMcpToolRegistry(services, configuration);

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

    private static void RegisterWorkflowEngine(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? hostEnvironment)
    {
        var mode = configuration["WorkflowEngine:Mode"];
        var isDevelopment = hostEnvironment is null || hostEnvironment.IsDevelopment();

        if (string.Equals(mode, "Stub", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IWorkflowEngine, StubWorkflowEngine>();
            return;
        }

        if (string.Equals(mode, "Real", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IWorkflowEngine, ChatClientWorkflowEngine>();
            return;
        }

        // mode is null/empty/Auto: pick based on whether a backend looks ready.
        var anyBackendConfigured = HasAnyBackendConfigured(configuration);
        if (anyBackendConfigured)
        {
            services.AddSingleton<IWorkflowEngine, ChatClientWorkflowEngine>();
            return;
        }

        if (isDevelopment)
        {
            services.AddSingleton<IWorkflowEngine, StubWorkflowEngine>();
            return;
        }

        throw new InvalidOperationException(
            "No LLM backend is configured and 'WorkflowEngine:Mode' is not set. " +
            "Set 'Backends:azure-openai:Endpoint' (or another backend's Endpoint) to enable " +
            "the real workflow engine, or set 'WorkflowEngine:Mode' to 'Stub' to use the stub.");
    }

    private static bool HasAnyBackendConfigured(IConfiguration configuration)
    {
        // Azure backends signal "really configured" via a non-empty Endpoint.
        // Key-based backends (openai/github-models/anthropic) cannot be auto-detected
        // because seed appsettings.json includes placeholder ApiKeyRef values; users
        // opt in via WorkflowEngine:Mode=Real for those.
        return !string.IsNullOrWhiteSpace(configuration["Backends:azure-openai:Endpoint"])
            || !string.IsNullOrWhiteSpace(configuration["Backends:azure-foundry:Endpoint"]);
    }

    private static void RegisterMcpToolRegistry(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(McpClientOptions.SectionName);
        var hasServers = section.GetSection("Servers").GetChildren().Any();

        if (!hasServers)
        {
            // Preserve historical behavior: no MCP servers configured → empty registry.
            services.AddSingleton<IMcpToolRegistry, EmptyMcpToolRegistry>();
            return;
        }

        services.AddOptions<McpClientOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IMcpServerSessionFactory>(sp =>
            new SdkMcpServerSessionFactory(
                sp.GetRequiredService<IBackendSecretResolver>(),
                sp.GetRequiredService<ILoggerFactory>()));

        services.AddSingleton<HttpMcpToolRegistry>();
        services.AddSingleton<IMcpToolRegistry>(sp => sp.GetRequiredService<HttpMcpToolRegistry>());
        // Ensure the host disposes the underlying SDK clients on shutdown.
        services.AddSingleton<IAsyncDisposable>(sp => sp.GetRequiredService<HttpMcpToolRegistry>());
    }

    private static void RegisterPersonaRepository(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? hostEnvironment)
    {
        var configured = configuration["Personas:Directory"];
        var contentRoot = hostEnvironment?.ContentRootPath ?? Directory.GetCurrentDirectory();
        var directory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(contentRoot, "personas")
            : (Path.IsPathFullyQualified(configured)
                ? configured
                : Path.Combine(contentRoot, configured));
        var watch = configuration.GetValue<bool?>("Personas:Watch") ?? true;
        var isDevelopment = hostEnvironment is null || hostEnvironment.IsDevelopment();

        if (Directory.Exists(directory))
        {
            services.AddSingleton<IPersonaRepository>(sp =>
                new YamlPersonaRepository(
                    directory,
                    sp.GetRequiredService<ILogger<YamlPersonaRepository>>(),
                    watch));
            return;
        }

        if (isDevelopment)
        {
            services.AddSingleton<IPersonaRepository>(sp =>
            {
                sp.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("CloudEngAgent.Infrastructure.PersonaRepository")
                    .LogWarning(
                        "Personas directory '{Directory}' not found; using InMemoryPersonaRepository " +
                        "(Development only).",
                        directory);
                return new InMemoryPersonaRepository();
            });
            return;
        }

        throw new InvalidOperationException(
            $"Personas directory '{directory}' does not exist. Set 'Personas:Directory' or " +
            "create the directory with at least one *.yaml file.");
    }
}
