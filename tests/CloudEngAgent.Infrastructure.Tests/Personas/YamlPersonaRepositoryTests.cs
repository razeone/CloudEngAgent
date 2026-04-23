using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Domain.Backends;
using CloudEngAgent.Infrastructure.Personas;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CloudEngAgent.Infrastructure.Tests.Personas;

public sealed class YamlPersonaRepositoryTests : IDisposable
{
    private readonly string _dir;

    public YamlPersonaRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "personas-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private string Write(string fileName, string yaml)
    {
        var path = Path.Combine(_dir, fileName);
        File.WriteAllText(path, yaml);
        return path;
    }

    private const string ValidYaml = """
        id: explorer
        name: Schema Explorer
        backend: azure-openai
        systemPrompt: |
          You discover database structure.
        tools: []
        guardrails:
          temperature: 0.1
        """;

    [Fact]
    public async Task LoadAll_ValidYaml_ReturnsPersona()
    {
        Write("explorer.yaml", ValidYaml);

        using var repo = new YamlPersonaRepository(_dir, NullLogger<YamlPersonaRepository>.Instance, watch: false);

        var persona = await repo.GetAsync("explorer", CancellationToken.None);

        persona.Should().NotBeNull();
        persona!.Name.Should().Be("Schema Explorer");
        persona.Backend.Should().Be(BackendId.AzureOpenAi);
        persona.Guardrails.Temperature.Should().Be(0.1);
        persona.SystemPrompt.Should().Contain("structure");
    }

    [Fact]
    public async Task ListAsync_ReturnsAllLoadedPersonas()
    {
        Write("a.yaml", ValidYaml);
        Write("b.yaml", ValidYaml.Replace("explorer", "analyst").Replace("Schema Explorer", "Query Analyst"));

        using var repo = new YamlPersonaRepository(_dir, NullLogger<YamlPersonaRepository>.Instance, watch: false);

        var ids = new List<string>();
        await foreach (var p in repo.ListAsync(CancellationToken.None))
        {
            ids.Add(p.Id);
        }

        ids.Should().BeEquivalentTo(new[] { "explorer", "analyst" });
    }

    [Fact]
    public void Constructor_MissingDirectory_Throws()
    {
        var bogus = Path.Combine(_dir, "does-not-exist");

        Action act = () => new YamlPersonaRepository(bogus, NullLogger<YamlPersonaRepository>.Instance, watch: false);

        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void LoadAll_MissingId_Throws()
    {
        Write("bad.yaml", """
            name: Nameless
            backend: azure-openai
            systemPrompt: hi
            """);

        Action act = () => new YamlPersonaRepository(_dir, NullLogger<YamlPersonaRepository>.Instance, watch: false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'id'*");
    }

    [Fact]
    public void LoadAll_MissingName_Throws()
    {
        Write("bad.yaml", """
            id: foo
            backend: azure-openai
            systemPrompt: hi
            """);

        Action act = () => new YamlPersonaRepository(_dir, NullLogger<YamlPersonaRepository>.Instance, watch: false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'name'*");
    }

    [Fact]
    public void LoadAll_UnknownBackend_Throws()
    {
        Write("bad.yaml", """
            id: foo
            name: Foo
            backend: unknown-llm
            systemPrompt: hi
            """);

        Action act = () => new YamlPersonaRepository(_dir, NullLogger<YamlPersonaRepository>.Instance, watch: false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*unknown-llm*");
    }

    [Fact]
    public void LoadAll_InvalidYaml_Throws()
    {
        Write("bad.yaml", "id: foo\n  bad: indentation\nname: x");

        Action act = () => new YamlPersonaRepository(_dir, NullLogger<YamlPersonaRepository>.Instance, watch: false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Invalid YAML*");
    }

    [Fact]
    public void LoadAll_DuplicateId_Throws()
    {
        Write("a.yaml", ValidYaml);
        Write("a-copy.yaml", ValidYaml);

        Action act = () => new YamlPersonaRepository(_dir, NullLogger<YamlPersonaRepository>.Instance, watch: false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate persona id*");
    }

    [Fact]
    public async Task Version_StableAcrossReloadsOfSameContent()
    {
        Write("explorer.yaml", ValidYaml);
        using var repo = new YamlPersonaRepository(_dir, NullLogger<YamlPersonaRepository>.Instance, watch: false);

        var v1 = (await repo.GetAsync("explorer", CancellationToken.None))!.Version;
        repo.ReloadNow();
        var v2 = (await repo.GetAsync("explorer", CancellationToken.None))!.Version;

        v2.Should().Be(v1);
    }

    [Fact]
    public async Task ReloadNow_AfterAddingFile_RaisesAddedEvent()
    {
        Write("explorer.yaml", ValidYaml);
        using var repo = new YamlPersonaRepository(_dir, NullLogger<YamlPersonaRepository>.Instance, watch: false);

        var changes = new List<PersonaChangedEventArgs>();
        repo.PersonaChanged += (_, e) => changes.Add(e);

        Write("analyst.yaml", ValidYaml.Replace("explorer", "analyst").Replace("Schema Explorer", "Query Analyst"));
        repo.ReloadNow();

        changes.Should().ContainSingle(c => c.PersonaId == "analyst" && c.Kind == PersonaChangeKind.Added);
        var analyst = await repo.GetAsync("analyst", CancellationToken.None);
        analyst.Should().NotBeNull();
    }

    [Fact]
    public void ReloadNow_AfterModifyingFile_RaisesUpdatedEvent()
    {
        Write("explorer.yaml", ValidYaml);
        using var repo = new YamlPersonaRepository(_dir, NullLogger<YamlPersonaRepository>.Instance, watch: false);

        var changes = new List<PersonaChangedEventArgs>();
        repo.PersonaChanged += (_, e) => changes.Add(e);

        Write("explorer.yaml", ValidYaml.Replace("Schema Explorer", "Schema Explorer v2"));
        repo.ReloadNow();

        changes.Should().ContainSingle(c => c.PersonaId == "explorer" && c.Kind == PersonaChangeKind.Updated);
    }

    [Fact]
    public void ReloadNow_AfterDeletingFile_RaisesRemovedEvent()
    {
        var path = Write("explorer.yaml", ValidYaml);
        using var repo = new YamlPersonaRepository(_dir, NullLogger<YamlPersonaRepository>.Instance, watch: false);

        var changes = new List<PersonaChangedEventArgs>();
        repo.PersonaChanged += (_, e) => changes.Add(e);

        File.Delete(path);
        repo.ReloadNow();

        changes.Should().ContainSingle(c => c.PersonaId == "explorer" && c.Kind == PersonaChangeKind.Removed);
    }

    [Fact]
    public async Task FileSystemWatcher_HotReloadsOnFileWrite()
    {
        Write("explorer.yaml", ValidYaml);
        using var repo = new YamlPersonaRepository(_dir, NullLogger<YamlPersonaRepository>.Instance, watch: true);

        using var signal = new ManualResetEventSlim(false);
        repo.PersonaChanged += (_, _) => signal.Set();

        Write("explorer.yaml", ValidYaml.Replace("Schema Explorer", "Schema Explorer v2"));

        signal.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("watcher should detect the change");

        var persona = await repo.GetAsync("explorer", CancellationToken.None);
        persona!.Name.Should().Be("Schema Explorer v2");
    }
}
