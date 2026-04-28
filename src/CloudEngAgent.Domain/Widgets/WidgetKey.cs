using System.Text.RegularExpressions;

namespace CloudEngAgent.Domain.Widgets;

public sealed partial record WidgetKey
{
    private static readonly Regex CanonicalPattern = BuildCanonicalRegex();

    public WidgetKey(Guid RunId, string StepId, string AgentId, string LocalId)
    {
        if (RunId == Guid.Empty)
        {
            throw new ArgumentException("RunId must be a non-empty Guid.", nameof(RunId));
        }

        if (string.IsNullOrWhiteSpace(StepId))
        {
            throw new ArgumentException("StepId must be non-empty.", nameof(StepId));
        }

        if (string.IsNullOrWhiteSpace(AgentId))
        {
            throw new ArgumentException("AgentId must be non-empty.", nameof(AgentId));
        }

        if (string.IsNullOrWhiteSpace(LocalId))
        {
            throw new ArgumentException("LocalId must be non-empty.", nameof(LocalId));
        }

        if (StepId.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException("StepId must not contain '/'.", nameof(StepId));
        }

        if (AgentId.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException("AgentId must not contain '/'.", nameof(AgentId));
        }

        if (LocalId.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException("LocalId must not contain '/'.", nameof(LocalId));
        }

        this.RunId = RunId;
        this.StepId = StepId;
        this.AgentId = AgentId;
        this.LocalId = LocalId;
    }

    public Guid RunId { get; init; }
    public string StepId { get; init; }
    public string AgentId { get; init; }
    public string LocalId { get; init; }

    public string ToCanonicalString() =>
        $"run:{RunId:D}/step:{StepId}/agent:{AgentId}/widget:{LocalId}";

    public override string ToString() => ToCanonicalString();

    public static WidgetKey Parse(string canonical)
    {
        if (string.IsNullOrWhiteSpace(canonical))
        {
            throw new FormatException("Canonical widget key must be non-empty.");
        }

        var match = CanonicalPattern.Match(canonical);
        if (!match.Success)
        {
            throw new FormatException(
                $"Invalid widget key '{canonical}'. Expected 'run:{{guid}}/step:{{stepId}}/agent:{{agentId}}/widget:{{localId}}'.");
        }

        if (!Guid.TryParseExact(match.Groups["run"].Value, "D", out var runId))
        {
            throw new FormatException(
                $"Invalid widget key '{canonical}': run id is not a valid Guid.");
        }

        try
        {
            return new WidgetKey(
                runId,
                match.Groups["step"].Value,
                match.Groups["agent"].Value,
                match.Groups["local"].Value);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"Invalid widget key '{canonical}': {ex.Message}", ex);
        }
    }

    [GeneratedRegex(@"^run:(?<run>[0-9a-fA-F\-]+)/step:(?<step>[^/]+)/agent:(?<agent>[^/]+)/widget:(?<local>[^/]+)$")]
    private static partial Regex BuildCanonicalRegex();
}
