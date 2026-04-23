using CloudEngAgent.Application.Mcp;

namespace CloudEngAgent.Infrastructure.Mcp;

/// <summary>
/// Thin abstraction over a connected MCP server client. Lets us mock the
/// ModelContextProtocol SDK in tests without dragging in its concrete types.
/// </summary>
internal interface IMcpServerSession : IAsyncDisposable
{
    /// <summary>Logical server name (matches <see cref="McpServerConfig.Name"/>).</summary>
    string Name { get; }

    /// <summary>
    /// Lists tools exposed by this server. Implementations should return a
    /// fully-materialised list (callers cache the result).
    /// </summary>
    Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Invokes a tool on this server. Implementations marshal MCP error
    /// responses to <see cref="McpToolInvocationResult.IsError"/> = <c>true</c>
    /// rather than throwing.
    /// </summary>
    Task<McpToolInvocationResult> CallToolAsync(
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken);
}

/// <summary>
/// Factory that lazily opens <see cref="IMcpServerSession"/> instances
/// (one per configured server). Sessions are cached and disposed by the registry.
/// </summary>
internal interface IMcpServerSessionFactory
{
    Task<IMcpServerSession> CreateAsync(McpServerConfig config, CancellationToken cancellationToken);
}
