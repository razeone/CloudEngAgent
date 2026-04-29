namespace CloudEngAgent.Infrastructure.Personas;

/// <summary>
/// On-disk YAML schema for an agent persona. Field names use camelCase to match
/// the YamlDotNet camelCase naming convention applied by <see cref="YamlPersonaRepository"/>.
/// </summary>
internal sealed class PersonaYamlDocument
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? SystemPrompt { get; set; }
    public string? Backend { get; set; }
    public List<string>? Tools { get; set; }
    public List<string>? AllowedWidgets { get; set; }
    public PersonaYamlGuardrails? Guardrails { get; set; }
}

internal sealed class PersonaYamlGuardrails
{
    public int? MaxTokens { get; set; }
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
}
