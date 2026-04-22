using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Application.Runs;
using CloudEngAgent.Infrastructure.Backends;
using CloudEngAgent.Infrastructure.Personas;
using CloudEngAgent.Infrastructure.Runs;
using CloudEngAgent.Infrastructure.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        services.AddScoped<StartWorkflowRunHandler>();

        return services;
    }
}
