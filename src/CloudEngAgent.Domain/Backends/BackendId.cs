namespace CloudEngAgent.Domain.Backends;

public sealed record BackendId
{
    private BackendId(string value) => Value = value;

    public string Value { get; }

    public static BackendId AzureOpenAi { get; } = new("azure-openai");
    public static BackendId AzureFoundry { get; } = new("azure-foundry");
    public static BackendId Anthropic { get; } = new("anthropic");
    public static BackendId GitHubModels { get; } = new("github-models");
    public static BackendId OpenAi { get; } = new("openai");
    public static BackendId Ollama { get; } = new("ollama");

    public static IReadOnlyList<BackendId> All { get; } = new[]
    {
        AzureOpenAi, AzureFoundry, Anthropic, GitHubModels, OpenAi, Ollama
    };

    public static BackendId Parse(string value)
    {
        if (!TryParse(value, out var id))
        {
            var known = string.Join(", ", All.Select(b => b.Value));
            throw new ArgumentException(
                $"Unknown backend id '{value}'. Known: {known}.",
                nameof(value));
        }

        return id;
    }

    public static bool TryParse(string? value, out BackendId backendId)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            foreach (var candidate in All)
            {
                if (string.Equals(candidate.Value, value, StringComparison.Ordinal))
                {
                    backendId = candidate;
                    return true;
                }
            }
        }

        backendId = AzureOpenAi;
        return false;
    }

    public override string ToString() => Value;
}
