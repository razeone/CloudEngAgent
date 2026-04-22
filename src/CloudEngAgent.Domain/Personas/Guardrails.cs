namespace CloudEngAgent.Domain.Personas;

public sealed record Guardrails(int? MaxTokens = null, double? Temperature = null, double? TopP = null)
{
    public static Guardrails Default { get; } = new();
}
