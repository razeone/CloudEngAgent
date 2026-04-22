using CloudEngAgent.Domain.Tools;

namespace CloudEngAgent.Application.Mcp;

public sealed record McpToolDescriptor(
    ToolRef ToolRef,
    string Description,
    string JsonSchema);
