using System.Text.Json;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Application.Mcp;
using CloudEngAgent.Domain.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SdkClientOptions = ModelContextProtocol.Client.McpClientOptions;

namespace CloudEngAgent.Infrastructure.Mcp;

/// <summary>
/// Default <see cref="IMcpServerSessionFactory"/> backed by the
/// ModelContextProtocol SDK's HTTP/SSE client transport.
/// </summary>
internal sealed class SdkMcpServerSessionFactory : IMcpServerSessionFactory
{
    private readonly IBackendSecretResolver _secretResolver;
    private readonly ILoggerFactory _loggerFactory;

    public SdkMcpServerSessionFactory(
        IBackendSecretResolver secretResolver,
        ILoggerFactory loggerFactory)
    {
        _secretResolver = secretResolver;
        _loggerFactory = loggerFactory;
    }

    public async Task<IMcpServerSession> CreateAsync(
        McpServerConfig config,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);

        var transportOptions = new SseClientTransportOptions
        {
            Name = config.Name,
            Endpoint = config.EndpointUri,
        };

        if (string.Equals(config.AuthType, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(config.TokenRef))
            {
                throw new InvalidOperationException(
                    $"MCP server '{config.Name}' has AuthType=Bearer but no TokenRef configured.");
            }

            var token = await _secretResolver.ResolveAsync(config.TokenRef, cancellationToken)
                .ConfigureAwait(false);
            transportOptions.AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {token}",
            };
        }

        var transport = new SseClientTransport(transportOptions, _loggerFactory);
        var clientOptions = new SdkClientOptions
        {
            ClientInfo = new Implementation { Name = "CloudEngAgent", Version = "1.0.0" },
        };

        var client = await McpClientFactory.CreateAsync(
                transport,
                clientOptions,
                _loggerFactory,
                cancellationToken)
            .ConfigureAwait(false);

        return new SdkMcpServerSession(config.Name, client);
    }
}

/// <summary>SDK-backed <see cref="IMcpServerSession"/>.</summary>
internal sealed class SdkMcpServerSession : IMcpServerSession
{
    private readonly IMcpClient _client;

    public SdkMcpServerSession(string name, IMcpClient client)
    {
        Name = name;
        _client = client;
    }

    public string Name { get; }

    public async Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken)
    {
        var tools = await _client.ListToolsAsync(serializerOptions: null, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var result = new List<McpToolDescriptor>(tools.Count);
        foreach (var tool in tools)
        {
            result.Add(new McpToolDescriptor(
                ToolRef: new ToolRef(Name, tool.Name),
                Description: tool.Description ?? string.Empty,
                JsonSchema: tool.JsonSchema.ValueKind == JsonValueKind.Undefined
                    ? "{}"
                    : tool.JsonSchema.GetRawText()));
        }
        return result;
    }

    public async Task<McpToolInvocationResult> CallToolAsync(
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, object?> args;
        try
        {
            args = ParseArguments(argumentsJson);
        }
        catch (JsonException ex)
        {
            return new McpToolInvocationResult(
                IsError: true,
                ResultJson: JsonSerializer.Serialize(new
                {
                    error = "invalid_arguments",
                    message = ex.Message,
                }));
        }

        // The SDK accepts IReadOnlyDictionary<string, object?>; null values are fine.
        var argDict = (IReadOnlyDictionary<string, object?>)args;

        CallToolResponse response;
        try
        {
            response = await _client.CallToolAsync(
                    toolName,
                    argDict,
                    progress: null,
                    serializerOptions: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (McpException ex)
        {
            return new McpToolInvocationResult(
                IsError: true,
                ResultJson: JsonSerializer.Serialize(new
                {
                    error = "mcp_exception",
                    code = (int)ex.ErrorCode,
                    message = ex.Message,
                }));
        }

        var json = JsonSerializer.Serialize(new
        {
            content = response.Content,
            isError = response.IsError,
        });
        return new McpToolInvocationResult(IsError: response.IsError, ResultJson: json);
    }

    private static IReadOnlyDictionary<string, object?> ParseArguments(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return new Dictionary<string, object?>();
        }

        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson);
        if (parsed is null)
        {
            return new Dictionary<string, object?>();
        }

        var result = new Dictionary<string, object?>(parsed.Count, StringComparer.Ordinal);
        foreach (var (k, v) in parsed)
        {
            result[k] = v.ValueKind == JsonValueKind.Null ? null : (object)v;
        }
        return result;
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
