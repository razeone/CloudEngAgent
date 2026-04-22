using CloudEngAgent.Application.Mcp;
using CloudEngAgent.Domain.Tools;

namespace CloudEngAgent.Application.Abstractions;

public interface IMcpToolRegistry
{
    IAsyncEnumerable<McpToolDescriptor> ListToolsAsync(CancellationToken cancellationToken);

    Task<McpToolInvocationResult> InvokeAsync(
        ToolRef toolRef,
        string argumentsJson,
        CancellationToken cancellationToken);
}
