using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CloudEngAgent.Infrastructure.Mcp;

/// <summary>
/// Options for the HTTP-based MCP tool registry. Bound to configuration
/// section <see cref="SectionName"/>.
/// </summary>
public sealed class McpClientOptions
{
    public const string SectionName = "Mcp:Client";

    /// <summary>The MCP servers this client connects to.</summary>
    [ValidateEnumeratedItems]
    public List<McpServerConfig> Servers { get; init; } = new();
}

/// <summary>
/// Configuration for a single MCP server endpoint. Validated when
/// <see cref="McpClientOptions"/> is bound.
/// </summary>
public sealed class McpServerConfig
{
    /// <summary>
    /// Logical server name. Used as the prefix in <c>mcp:&lt;name&gt;.&lt;tool&gt;</c>
    /// tool references. Must match <c>^[a-zA-Z0-9_-]+$</c>.
    /// </summary>
    [Required]
    [RegularExpression("^[a-zA-Z0-9_-]+$",
        ErrorMessage = "MCP server Name must match ^[a-zA-Z0-9_-]+$.")]
    public string Name { get; init; } = string.Empty;

    /// <summary>HTTP(S) endpoint of the MCP server (e.g. http://localhost:5010/mcp).</summary>
    [Required]
    [Url]
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>Auth scheme. Either <c>None</c> (default) or <c>Bearer</c>.</summary>
    [RegularExpression("^(None|Bearer)$",
        ErrorMessage = "MCP server AuthType must be 'None' or 'Bearer'.")]
    public string AuthType { get; init; } = "None";

    /// <summary>
    /// Secret reference (resolved via <see cref="Application.Abstractions.IBackendSecretResolver"/>)
    /// holding the bearer token. Required when <see cref="AuthType"/> is <c>Bearer</c>.
    /// </summary>
    public string? TokenRef { get; init; }

    /// <summary>Returns the parsed endpoint URI. Throws if <see cref="Endpoint"/> is invalid.</summary>
    public Uri EndpointUri => new(Endpoint, UriKind.Absolute);
}
