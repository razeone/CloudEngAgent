using System.Runtime.CompilerServices;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Application.Mcp;
using CloudEngAgent.Domain.Backends;
using CloudEngAgent.Domain.Tools;
using Microsoft.Extensions.AI;

namespace CloudEngAgent.Infrastructure.Backends;

/// <summary>
/// Placeholder <see cref="IChatClientFactory"/>. Real implementations for
/// azure-openai, openai, and github-models are added in the LLM-backends plan.
/// Throws if invoked so misconfiguration is caught loudly during integration.
/// </summary>
public sealed class NotImplementedChatClientFactory : IChatClientFactory
{
    public IChatClient Create(BackendId backend) =>
        throw new NotImplementedException(
            $"Chat client factory for backend '{backend}' is not yet implemented in this milestone.");
}

/// <summary>
/// Placeholder <see cref="IMcpToolRegistry"/>. The real MCP client wiring is
/// delivered in the MCP plan; for now it advertises no tools and rejects any
/// invocation attempt.
/// </summary>
public sealed class EmptyMcpToolRegistry : IMcpToolRegistry
{
    public async IAsyncEnumerable<McpToolDescriptor> ListToolsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    public Task<McpToolInvocationResult> InvokeAsync(
        ToolRef toolRef,
        string argumentsJson,
        CancellationToken cancellationToken) =>
        Task.FromResult(new McpToolInvocationResult(
            IsError: true,
            ResultJson: $"{{\"error\":\"MCP registry is empty in this milestone; tool '{toolRef}' is unavailable.\"}}"));
}
