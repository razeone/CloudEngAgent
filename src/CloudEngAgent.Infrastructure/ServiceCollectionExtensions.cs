using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Application.Runs;
using CloudEngAgent.Infrastructure.Backends;
using CloudEngAgent.Infrastructure.Personas;
using CloudEngAgent.Infrastructure.Runs;
using CloudEngAgent.Infrastructure.Sse;
using CloudEngAgent.Infrastructure.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IClock, SystemClock>();

        services.AddSingleton<InMemoryRunStore>();
        services.AddSingleton<IRunStore>(sp => sp.GetRequiredService<InMemoryRunStore>());

        services.AddSingleton<IRunEventBus, InMemoryRunEventBus>();

        services.AddSingleton<IPersonaRepository, InMemoryPersonaRepository>();
        services.AddSingleton<IWorkflowRegistry, InMemoryWorkflowRegistry>();
        services.AddSingleton<IWorkflowEngine, StubWorkflowEngine>();

        services.AddSingleton<IChatClientFactory, NotImplementedChatClientFactory>();
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
}
