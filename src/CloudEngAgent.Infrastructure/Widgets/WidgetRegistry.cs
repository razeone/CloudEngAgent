using System.Reflection;
using System.Text;
using System.Text.Json;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Domain.Widgets;
using Json.Schema;
using Microsoft.Extensions.Logging;

namespace CloudEngAgent.Infrastructure.Widgets;

/// <summary>
/// Loads the embedded JSON Schema for each <see cref="WidgetType"/>, caches a
/// compiled <see cref="JsonSchema"/> per type, and enforces per-type policies
/// + per-persona allow-lists. Pure CPU-bound; safe to register as a singleton.
/// </summary>
public sealed class WidgetRegistry : IWidgetRegistry
{
    private const string SchemaResourcePrefix =
        "CloudEngAgent.Infrastructure.Widgets.Schemas.";

    private static readonly EvaluationOptions EvaluationOptions = new()
    {
        OutputFormat = OutputFormat.List,
    };

    private static readonly Lazy<Dictionary<string, SchemaEntry>> SchemaEntries =
        new(LoadAllSchemas, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Dictionary<string, SchemaEntry> _entries;
    private readonly IReadOnlyDictionary<string, WidgetPolicy> _policies;
    private readonly IPersonaWidgetPolicy _personaPolicy;
    private readonly ILogger<WidgetRegistry> _logger;

    public WidgetRegistry(IPersonaWidgetPolicy personaPolicy, ILogger<WidgetRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(personaPolicy);
        ArgumentNullException.ThrowIfNull(logger);

        _personaPolicy = personaPolicy;
        _logger = logger;
        _policies = DefaultPolicies;
        _entries = SchemaEntries.Value;

        _logger.LogInformation(
            "WidgetRegistry loaded {Count} widget schemas: {Types}",
            _entries.Count,
            string.Join(", ", _entries.Keys));
    }

    public IReadOnlyList<WidgetType> Types { get; } = WidgetType.All;

    public string GetSchemaJson(WidgetType type) => GetEntry(type).Json;

    public WidgetPolicy GetPolicy(WidgetType type)
    {
        if (!_policies.TryGetValue(type.Value, out var policy))
        {
            throw new ArgumentException(
                $"No policy registered for widget type '{type.Value}'.", nameof(type));
        }

        return policy;
    }

    public WidgetValidationResult Validate(WidgetType type, string propsJson)
    {
        ArgumentNullException.ThrowIfNull(propsJson);

        var entry = GetEntry(type);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(propsJson);
        }
        catch (JsonException ex)
        {
            return new WidgetValidationResult(false, new[] { $"Invalid JSON: {ex.Message}" });
        }

        using (doc)
        {
            var result = entry.Schema.Evaluate(doc.RootElement, EvaluationOptions);
            if (result.IsValid)
            {
                return new WidgetValidationResult(true, Array.Empty<string>());
            }

            var errors = new List<string>();
            CollectErrors(result, errors);
            if (errors.Count == 0)
            {
                errors.Add("Validation failed.");
            }

            return new WidgetValidationResult(false, errors);
        }
    }

    public bool IsAllowed(string personaId, WidgetType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personaId);

        var policy = GetPolicy(type);
        if (policy.AllowedPersonas.Count > 0
            && !policy.AllowedPersonas.Contains(personaId, StringComparer.Ordinal))
        {
            return false;
        }

        var personaAllowed = _personaPolicy.GetAllowedWidgets(personaId);
        if (personaAllowed is null)
        {
            return true;
        }

        foreach (var allowed in personaAllowed)
        {
            if (string.Equals(allowed.Value, type.Value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private SchemaEntry GetEntry(WidgetType type)
    {
        if (!_entries.TryGetValue(type.Value, out var entry))
        {
            throw new ArgumentException(
                $"No schema registered for widget type '{type.Value}'.", nameof(type));
        }

        return entry;
    }

    private static void CollectErrors(EvaluationResults result, List<string> errors)
    {
        if (result.Errors is { Count: > 0 } resultErrors)
        {
            foreach (var (key, message) in resultErrors)
            {
                var location = result.InstanceLocation.ToString();
                errors.Add(string.IsNullOrEmpty(location)
                    ? $"{key}: {message}"
                    : $"{location}: {key}: {message}");
            }
        }

        if (result.Details is null)
        {
            return;
        }

        foreach (var detail in result.Details)
        {
            CollectErrors(detail, errors);
        }
    }

    private static Dictionary<string, SchemaEntry> LoadAllSchemas()
    {
        var assembly = typeof(WidgetRegistry).Assembly;
        var entries = new Dictionary<string, SchemaEntry>(StringComparer.Ordinal);

        foreach (var type in WidgetType.All)
        {
            var resourceName = SchemaResourcePrefix + type.Value + ".schema.json";
            var json = ReadResource(assembly, resourceName);

            JsonSchema schema;
            try
            {
                schema = JsonSchema.FromText(json);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Embedded widget schema '{resourceName}' is not valid JSON Schema: {ex.Message}",
                    ex);
            }

            entries[type.Value] = new SchemaEntry(json, schema);
        }

        return entries;
    }

    private static string ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded widget schema resource '{resourceName}' was not found. "
                + "Verify the file is marked as <EmbeddedResource> in the .csproj.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private sealed record SchemaEntry(string Json, JsonSchema Schema);

    /// <summary>
    /// Per-widget-type policy table. Empty <c>AllowedPersonas</c> means any
    /// persona is allowed (subject to the persona's own allow-list).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, WidgetPolicy> DefaultPolicies =
        new Dictionary<string, WidgetPolicy>(StringComparer.Ordinal)
        {
            ["result-table"] = new(
                MaxPayloadBytes: 1_048_576,
                MaxRows: 5_000,
                MaxSeries: null,
                CanRequestInput: false,
                CanAttachArtifacts: true,
                AllowedPersonas: Array.Empty<string>()),
            ["bar-chart"] = new(
                MaxPayloadBytes: 262_144,
                MaxRows: null,
                MaxSeries: 50,
                CanRequestInput: false,
                CanAttachArtifacts: false,
                AllowedPersonas: Array.Empty<string>()),
            ["kpi-cards"] = new(
                MaxPayloadBytes: 65_536,
                MaxRows: null,
                MaxSeries: null,
                CanRequestInput: false,
                CanAttachArtifacts: false,
                AllowedPersonas: Array.Empty<string>()),
            ["findings-list"] = new(
                MaxPayloadBytes: 524_288,
                MaxRows: 1_000,
                MaxSeries: null,
                CanRequestInput: false,
                CanAttachArtifacts: true,
                AllowedPersonas: Array.Empty<string>()),
            ["ddl-diff"] = new(
                MaxPayloadBytes: 262_144,
                MaxRows: null,
                MaxSeries: null,
                CanRequestInput: false,
                CanAttachArtifacts: false,
                AllowedPersonas: Array.Empty<string>()),
            ["approval-card"] = new(
                MaxPayloadBytes: 65_536,
                MaxRows: null,
                MaxSeries: null,
                CanRequestInput: true,
                CanAttachArtifacts: false,
                AllowedPersonas: Array.Empty<string>()),
            ["file-download"] = new(
                MaxPayloadBytes: 8_192,
                MaxRows: null,
                MaxSeries: null,
                CanRequestInput: false,
                CanAttachArtifacts: true,
                AllowedPersonas: Array.Empty<string>()),
            ["markdown-report"] = new(
                MaxPayloadBytes: 524_288,
                MaxRows: null,
                MaxSeries: null,
                CanRequestInput: false,
                CanAttachArtifacts: true,
                AllowedPersonas: Array.Empty<string>()),
        };
}
