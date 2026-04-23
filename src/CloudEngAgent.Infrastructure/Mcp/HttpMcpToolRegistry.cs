using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Application.Mcp;
using CloudEngAgent.Domain.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloudEngAgent.Infrastructure.Mcp;

/// <summary>
/// HTTP-backed <see cref="IMcpToolRegistry"/> that fans out tool listings and
/// invocations across one or more MCP servers configured under
/// <c>Mcp:Client:Servers</c>. Tool references are namespaced as
/// <c>mcp:&lt;server&gt;.&lt;tool&gt;</c>.
/// </summary>
internal sealed class HttpMcpToolRegistry : IMcpToolRegistry, IAsyncDisposable
{
    private static readonly TimeSpan ToolListTtl = TimeSpan.FromSeconds(60);

    private readonly IReadOnlyList<McpServerConfig> _servers;
    private readonly IMcpServerSessionFactory _sessionFactory;
    private readonly ILogger<HttpMcpToolRegistry> _logger;

    private readonly ConcurrentDictionary<string, Lazy<Task<IMcpServerSession>>> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private DateTimeOffset _toolCacheExpiry = DateTimeOffset.MinValue;
    private IReadOnlyList<McpToolDescriptor>? _toolCache;

    private int _disposed;

    public HttpMcpToolRegistry(
        IOptions<McpClientOptions> options,
        IMcpServerSessionFactory sessionFactory,
        ILogger<HttpMcpToolRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _servers = options.Value.Servers ?? new List<McpServerConfig>();
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<McpToolDescriptor> ListToolsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var snapshot = await GetOrLoadToolsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var tool in snapshot)
        {
            yield return tool;
        }
    }

    /// <inheritdoc />
    public async Task<McpToolInvocationResult> InvokeAsync(
        ToolRef toolRef,
        string argumentsJson,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(toolRef);

        var server = _servers.FirstOrDefault(s =>
            string.Equals(s.Name, toolRef.ServerName, StringComparison.Ordinal));
        if (server is null)
        {
            _logger.LogWarning(
                "MCP tool invocation rejected: no server '{Server}' is configured (tool '{Tool}').",
                toolRef.ServerName,
                toolRef);
            return new McpToolInvocationResult(
                IsError: true,
                ResultJson: $"{{\"error\":\"unknown_server\",\"server\":\"{toolRef.ServerName}\"}}");
        }

        IMcpServerSession session;
        try
        {
            session = await GetOrCreateSessionAsync(server, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Failed to open MCP session to server '{Server}' for tool '{Tool}'.",
                server.Name,
                toolRef);
            return new McpToolInvocationResult(
                IsError: true,
                ResultJson: $"{{\"error\":\"server_unreachable\",\"server\":\"{server.Name}\"}}");
        }

        return await session.CallToolAsync(toolRef.ToolName, argumentsJson, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Drops the in-memory tool listing cache so the next
    /// <see cref="ListToolsAsync"/> call reloads from the upstream servers.
    /// </summary>
    public void InvalidateCache()
    {
        _cacheLock.Wait();
        try
        {
            _toolCache = null;
            _toolCacheExpiry = DateTimeOffset.MinValue;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task<IReadOnlyList<McpToolDescriptor>> GetOrLoadToolsAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (_toolCache is { } cached && now < _toolCacheExpiry)
        {
            return cached;
        }

        await _cacheLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_toolCache is { } stillValid && DateTimeOffset.UtcNow < _toolCacheExpiry)
            {
                return stillValid;
            }

            var aggregated = new Dictionary<string, McpToolDescriptor>(StringComparer.Ordinal);
            foreach (var server in _servers)
            {
                IMcpServerSession session;
                try
                {
                    session = await GetOrCreateSessionAsync(server, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "MCP server '{Server}' is unreachable; skipping during tool listing.",
                        server.Name);
                    continue;
                }

                IReadOnlyList<McpToolDescriptor> tools;
                try
                {
                    tools = await session.ListToolsAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "MCP server '{Server}' failed during ListToolsAsync; skipping.",
                        server.Name);
                    // Drop the broken session so the next attempt reconnects.
                    InvalidateSession(server.Name);
                    continue;
                }

                foreach (var tool in tools)
                {
                    var key = tool.ToolRef.Qualified;
                    if (aggregated.ContainsKey(key))
                    {
                        _logger.LogWarning(
                            "MCP tool name collision on '{Tool}'; previous descriptor will be overwritten by '{Server}'.",
                            key,
                            server.Name);
                    }
                    aggregated[key] = tool;
                }
            }

            _toolCache = aggregated.Values.ToArray();
            _toolCacheExpiry = DateTimeOffset.UtcNow.Add(ToolListTtl);
            return _toolCache;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private Task<IMcpServerSession> GetOrCreateSessionAsync(McpServerConfig config, CancellationToken ct)
    {
        var lazy = _sessions.GetOrAdd(
            config.Name,
            _ => new Lazy<Task<IMcpServerSession>>(
                () => _sessionFactory.CreateAsync(config, ct),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }

    private void InvalidateSession(string name)
    {
        if (_sessions.TryRemove(name, out var lazy) && lazy.IsValueCreated)
        {
            _ = SafeDisposeAsync(lazy.Value);
        }
    }

    private static async Task SafeDisposeAsync(Task<IMcpServerSession> sessionTask)
    {
        try
        {
            var session = await sessionTask.ConfigureAwait(false);
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Disposal of a broken session is best-effort.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var lazy in _sessions.Values)
        {
            if (!lazy.IsValueCreated) continue;
            await SafeDisposeAsync(lazy.Value).ConfigureAwait(false);
        }
        _sessions.Clear();
        _cacheLock.Dispose();
    }
}
