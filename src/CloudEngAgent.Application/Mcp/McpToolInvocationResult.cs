namespace CloudEngAgent.Application.Mcp;

public sealed record McpToolInvocationResult(
    bool IsError,
    string ResultJson);
