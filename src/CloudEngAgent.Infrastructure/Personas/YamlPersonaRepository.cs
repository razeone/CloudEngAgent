using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Domain.Backends;
using CloudEngAgent.Domain.Personas;
using CloudEngAgent.Domain.Tools;
using CloudEngAgent.Domain.Widgets;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CloudEngAgent.Infrastructure.Personas;

/// <summary>
/// Loads agent personas from a directory of <c>*.yaml</c> files and watches the
/// directory for changes. Each file describes a single persona; the file's id
/// must match the persona's <c>id</c> field. Hot reload raises
/// <see cref="PersonaChanged"/> for adds, updates, and removals.
/// </summary>
public sealed class YamlPersonaRepository : IPersonaRepository, IPersonaWidgetPolicy, IDisposable
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(500);

    private readonly string _directory;
    private readonly ILogger<YamlPersonaRepository> _logger;
    private readonly FileSystemWatcher? _watcher;
    private readonly object _reloadLock = new();
    private readonly Timer _debounceTimer;
    private IReadOnlyDictionary<string, AgentPersona> _snapshot =
        new Dictionary<string, AgentPersona>(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, IReadOnlyList<WidgetType>> _allowedWidgets =
        new Dictionary<string, IReadOnlyList<WidgetType>>(StringComparer.Ordinal);

    public YamlPersonaRepository(string directory, ILogger<YamlPersonaRepository> logger, bool watch = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(logger);

        _directory = Path.GetFullPath(directory);
        _logger = logger;
        _debounceTimer = new Timer(_ => SafeReload(), null, Timeout.Infinite, Timeout.Infinite);

        if (!Directory.Exists(_directory))
        {
            throw new DirectoryNotFoundException(
                $"Personas directory '{_directory}' does not exist.");
        }

        Reload(initial: true);

        if (watch)
        {
            _watcher = new FileSystemWatcher(_directory, "*.yaml")
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.LastWrite
                    | NotifyFilters.FileName
                    | NotifyFilters.CreationTime
                    | NotifyFilters.Size,
            };
            _watcher.Created += OnFsEvent;
            _watcher.Changed += OnFsEvent;
            _watcher.Deleted += OnFsEvent;
            _watcher.Renamed += OnFsEvent;
            _watcher.Error += (_, e) =>
                _logger.LogError(e.GetException(), "FileSystemWatcher error in {Directory}", _directory);
            _watcher.EnableRaisingEvents = true;
        }
    }

    public event EventHandler<PersonaChangedEventArgs>? PersonaChanged;

    public Task<AgentPersona?> GetAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var snapshot = Volatile.Read(ref _snapshot);
        return Task.FromResult(snapshot.TryGetValue(id, out var persona) ? persona : null);
    }

    public async IAsyncEnumerable<AgentPersona> ListAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        foreach (var persona in snapshot.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return persona;
            await Task.Yield();
        }
    }

    /// <summary>
    /// Forces an immediate (non-debounced) reload. Intended for tests.
    /// </summary>
    public void ReloadNow() => SafeReload();

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounceTimer.Dispose();
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        // Coalesce bursts of FS events (editors often fire several per save).
        _debounceTimer.Change(DebounceWindow, Timeout.InfiniteTimeSpan);
    }

    private void SafeReload()
    {
        try
        {
            Reload(initial: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Persona reload failed; keeping previous snapshot");
        }
    }

    private void Reload(bool initial)
    {
        lock (_reloadLock)
        {
            var previous = _snapshot;
            var (next, nextAllowed) = LoadAllInternal(_directory);

            Volatile.Write(ref _snapshot, next);
            Volatile.Write(ref _allowedWidgets, nextAllowed);

            if (initial)
            {
                _logger.LogInformation("Loaded {Count} personas from {Directory}", next.Count, _directory);
                return;
            }

            // Diff and raise events.
            foreach (var (id, persona) in next)
            {
                if (!previous.TryGetValue(id, out var prev))
                {
                    Raise(id, PersonaChangeKind.Added);
                }
                else if (!prev.Version.Equals(persona.Version))
                {
                    Raise(id, PersonaChangeKind.Updated);
                }
            }

            foreach (var id in previous.Keys)
            {
                if (!next.ContainsKey(id))
                {
                    Raise(id, PersonaChangeKind.Removed);
                }
            }
        }
    }

    private void Raise(string personaId, PersonaChangeKind kind)
    {
        _logger.LogInformation("Persona {PersonaId} {Kind}", personaId, kind);
        try
        {
            PersonaChanged?.Invoke(this, new PersonaChangedEventArgs(personaId, kind));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PersonaChanged handler threw for {PersonaId}", personaId);
        }
    }

    internal static IReadOnlyDictionary<string, AgentPersona> LoadAll(string directory)
        => LoadAllInternal(directory).Personas;

    public IReadOnlyList<WidgetType>? GetAllowedWidgets(string personaId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personaId);
        var allowed = Volatile.Read(ref _allowedWidgets);
        return allowed.TryGetValue(personaId, out var list) ? list : null;
    }

    internal static (IReadOnlyDictionary<string, AgentPersona> Personas,
        IReadOnlyDictionary<string, IReadOnlyList<WidgetType>> AllowedWidgets)
        LoadAllInternal(string directory)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var personas = new Dictionary<string, AgentPersona>(StringComparer.Ordinal);
        var allowed = new Dictionary<string, IReadOnlyList<WidgetType>>(StringComparer.Ordinal);
        var files = Directory.EnumerateFiles(directory, "*.yaml", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.Ordinal);

        foreach (var path in files)
        {
            string content;
            try
            {
                content = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Editor may still hold the file open; skip this round, the watcher
                // will retrigger when the writer releases the lock.
                continue;
            }

            PersonaYamlDocument? doc;
            try
            {
                doc = deserializer.Deserialize<PersonaYamlDocument>(content);
            }
            catch (YamlException ex)
            {
                throw new InvalidOperationException(
                    $"Invalid YAML in '{Path.GetFileName(path)}': {ex.Message}", ex);
            }

            if (doc is null)
            {
                throw new InvalidOperationException(
                    $"Persona file '{Path.GetFileName(path)}' is empty.");
            }

            var persona = MapPersona(doc, path, content);
            if (!personas.TryAdd(persona.Id, persona))
            {
                throw new InvalidOperationException(
                    $"Duplicate persona id '{persona.Id}' (file '{Path.GetFileName(path)}').");
            }

            if (doc.AllowedWidgets is { Count: > 0 } widgetIds)
            {
                var parsed = new List<WidgetType>(widgetIds.Count);
                foreach (var raw in widgetIds)
                {
                    try
                    {
                        parsed.Add(WidgetType.Parse(raw));
                    }
                    catch (ArgumentException ex)
                    {
                        throw new InvalidOperationException(
                            $"Persona '{persona.Id}' allowedWidgets contains an unknown widget type: {ex.Message}",
                            ex);
                    }
                }

                allowed[persona.Id] = parsed;
            }
        }

        return (personas, allowed);
    }

    private static AgentPersona MapPersona(PersonaYamlDocument doc, string path, string rawContent)
    {
        if (string.IsNullOrWhiteSpace(doc.Id))
        {
            throw new InvalidOperationException(
                $"Persona file '{Path.GetFileName(path)}' is missing required field 'id'.");
        }

        if (string.IsNullOrWhiteSpace(doc.Name))
        {
            throw new InvalidOperationException(
                $"Persona '{doc.Id}' is missing required field 'name'.");
        }

        if (string.IsNullOrWhiteSpace(doc.SystemPrompt))
        {
            throw new InvalidOperationException(
                $"Persona '{doc.Id}' is missing required field 'systemPrompt'.");
        }

        if (string.IsNullOrWhiteSpace(doc.Backend) || !BackendId.TryParse(doc.Backend, out var backend))
        {
            var known = string.Join(", ", BackendId.All.Select(b => b.Value));
            throw new InvalidOperationException(
                $"Persona '{doc.Id}' has unknown backend '{doc.Backend}'. Known: {known}.");
        }

        var tools = (doc.Tools ?? new List<string>())
            .Select(t => ToolRef.Parse(t))
            .ToArray();

        var guardrails = doc.Guardrails is null
            ? Guardrails.Default
            : new Guardrails(doc.Guardrails.MaxTokens, doc.Guardrails.Temperature, doc.Guardrails.TopP);

        var version = PersonaVersion.FromContentHash(SHA256.HashData(Encoding.UTF8.GetBytes(rawContent)));

        return new AgentPersona(
            Id: doc.Id,
            Name: doc.Name,
            SystemPrompt: doc.SystemPrompt,
            Backend: backend,
            Tools: tools,
            Guardrails: guardrails,
            Version: version);
    }
}
