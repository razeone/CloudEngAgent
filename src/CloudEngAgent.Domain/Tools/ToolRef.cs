namespace CloudEngAgent.Domain.Tools;

public sealed record ToolRef(string ServerName, string ToolName)
{
    private const string Prefix = "mcp:";

    public string Qualified => $"{Prefix}{ServerName}.{ToolName}";

    public static ToolRef Parse(string value)
    {
        if (!TryParse(value, out var toolRef))
        {
            throw new ArgumentException(
                $"Invalid tool reference '{value}'. Expected format 'mcp:<server>.<tool>'.",
                nameof(value));
        }

        return toolRef;
    }

    public static bool TryParse(string? value, out ToolRef toolRef)
    {
        toolRef = new ToolRef(string.Empty, string.Empty);

        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var body = value[Prefix.Length..];
        var dot = body.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || dot == body.Length - 1)
        {
            return false;
        }

        var server = body[..dot];
        var tool = body[(dot + 1)..];
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(tool))
        {
            return false;
        }

        toolRef = new ToolRef(server, tool);
        return true;
    }

    public override string ToString() => Qualified;
}
