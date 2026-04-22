using System.Runtime.CompilerServices;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Domain.Backends;
using CloudEngAgent.Domain.Personas;
using CloudEngAgent.Domain.Tools;

namespace CloudEngAgent.Infrastructure.Personas;

/// <summary>
/// Seed implementation of <see cref="IPersonaRepository"/>. Returns the six
/// DBA personas hard-coded for the API milestone. A YAML-backed implementation
/// with hot reload replaces this in a later plan.
/// </summary>
public sealed class InMemoryPersonaRepository : IPersonaRepository
{
    private readonly Dictionary<string, AgentPersona> _personas;

    public InMemoryPersonaRepository()
    {
        _personas = SeedPersonas().ToDictionary(p => p.Id, StringComparer.Ordinal);
    }

    public Task<AgentPersona?> GetAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Task.FromResult(_personas.TryGetValue(id, out var persona) ? persona : null);
    }

    public async IAsyncEnumerable<AgentPersona> ListAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var persona in _personas.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return persona;
            await Task.Yield();
        }
    }

    internal static IEnumerable<AgentPersona> SeedPersonas()
    {
        yield return Make(
            "orchestrator",
            "Orchestrator",
            "You route DBA requests to the correct sub-agent (Explorer, Analyst, Performance, Assessor, Administrator). Decide based on intent and produce a short handoff message.");
        yield return Make(
            "explorer",
            "Schema Explorer",
            "You discover database structure: schemas, tables, views, columns, indexes, foreign keys. Use the SQL MCP tools to introspect; never modify data.");
        yield return Make(
            "analyst",
            "Query Analyst",
            "You analyze SQL queries: parse, explain plans, predicate selectivity, and likely bottlenecks. Recommend rewrites; do not execute DDL.");
        yield return Make(
            "performance",
            "Performance Engineer",
            "You diagnose performance: missing/duplicate indexes, wait stats, blocking, parameter sniffing. Propose targeted, reversible changes.");
        yield return Make(
            "assessor",
            "Best-Practices Assessor",
            "You audit databases against security, compliance, and operational best practices. Produce a prioritized findings list with severity and remediation.");
        yield return Make(
            "administrator",
            "Administrator (read-only)",
            "You report on backups, users, roles, configuration, and high-availability state. Do not perform destructive operations in this phase.");
    }

    private static AgentPersona Make(string id, string name, string prompt) => new(
        Id: id,
        Name: name,
        SystemPrompt: prompt,
        Backend: BackendId.AzureOpenAi,
        Tools: Array.Empty<ToolRef>(),
        Guardrails: Guardrails.Default,
        Version: PersonaVersion.FromContentHash(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(id + "\n" + prompt))));
}
