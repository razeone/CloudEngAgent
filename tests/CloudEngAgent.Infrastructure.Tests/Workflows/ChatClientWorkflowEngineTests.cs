using System.Runtime.CompilerServices;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Application.Runs;
using CloudEngAgent.Domain.Backends;
using CloudEngAgent.Domain.Personas;
using CloudEngAgent.Domain.Runs;
using CloudEngAgent.Domain.Tools;
using CloudEngAgent.Domain.Workflows;
using CloudEngAgent.Infrastructure.Workflows;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace CloudEngAgent.Infrastructure.Tests.Workflows;

public sealed class ChatClientWorkflowEngineTests
{
    private static readonly AgentPersona TestPersona = new(
        Id: "orchestrator",
        Name: "Orchestrator",
        SystemPrompt: "You route DBA requests.",
        Backend: BackendId.AzureOpenAi,
        Tools: Array.Empty<ToolRef>(),
        Guardrails: new Guardrails(MaxTokens: 256, Temperature: 0.2, TopP: null),
        Version: PersonaVersion.FromContentHash(System.Security.Cryptography.SHA256.HashData(new byte[] { 1 })));

    private static readonly WorkflowDefinition TestWorkflow = new(
        Id: "dba-default",
        Name: "DBA Default",
        PersonaIds: new[] { "orchestrator" },
        EntryPersonaId: "orchestrator");

    private static readonly StartWorkflowRunInput TestInput = new(
        WorkflowId: "dba-default",
        UserInput: "List databases",
        ThreadId: "t-1",
        RequestedBy: "alice");

    private static IPersonaRepository PersonaRepoWith(AgentPersona? persona)
    {
        var repo = Substitute.For<IPersonaRepository>();
        repo.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(persona));
        return repo;
    }

    private static IClock FixedClock() => new FixedClockImpl(DateTimeOffset.Parse("2025-01-01T00:00:00Z"));

    private sealed class FixedClockImpl(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamFrom(
        IEnumerable<string> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var c in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(ChatRole.Assistant, c);
            await Task.Yield();
        }
    }

    [Fact]
    public async Task ExecuteAsync_StreamsTextDeltaPerChunk()
    {
        var personaRepo = PersonaRepoWith(TestPersona);
        var chatClient = Substitute.For<IChatClient>();
        chatClient.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => StreamFrom(new[] { "Hello", " world", "!" }));

        var factory = Substitute.For<IChatClientFactory>();
        factory.Create(BackendId.AzureOpenAi).Returns(chatClient);

        var engine = new ChatClientWorkflowEngine(personaRepo, factory, FixedClock(), NullLogger<ChatClientWorkflowEngine>.Instance);

        var events = new List<RunEvent>();
        await foreach (var e in engine.ExecuteAsync(TestWorkflow, TestInput, Guid.NewGuid(), CancellationToken.None))
        {
            events.Add(e);
        }

        events.Should().HaveCount(4);
        events[0].Type.Should().Be(RunEventType.AgentHandoff);
        events.Skip(1).Should().AllSatisfy(e => e.Type.Should().Be(RunEventType.TextDelta));
        events[1].PayloadJson.Should().Contain("Hello");
        events[2].PayloadJson.Should().Contain("world");
        events[3].PayloadJson.Should().Contain("!");
    }

    [Fact]
    public async Task ExecuteAsync_SkipsEmptyTextChunks()
    {
        var personaRepo = PersonaRepoWith(TestPersona);
        var chatClient = Substitute.For<IChatClient>();
        chatClient.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => StreamFrom(new[] { "", "data", "" }));

        var factory = Substitute.For<IChatClientFactory>();
        factory.Create(BackendId.AzureOpenAi).Returns(chatClient);

        var engine = new ChatClientWorkflowEngine(personaRepo, factory, FixedClock(), NullLogger<ChatClientWorkflowEngine>.Instance);

        var events = new List<RunEvent>();
        await foreach (var e in engine.ExecuteAsync(TestWorkflow, TestInput, Guid.NewGuid(), CancellationToken.None))
        {
            events.Add(e);
        }

        events.Should().HaveCount(2);
        events[1].PayloadJson.Should().Contain("data");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownEntryPersona_Throws()
    {
        var personaRepo = PersonaRepoWith(null);
        var factory = Substitute.For<IChatClientFactory>();
        var engine = new ChatClientWorkflowEngine(personaRepo, factory, FixedClock(), NullLogger<ChatClientWorkflowEngine>.Instance);

        var act = async () =>
        {
            await foreach (var _ in engine.ExecuteAsync(TestWorkflow, TestInput, Guid.NewGuid(), CancellationToken.None))
            {
            }
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*orchestrator*");
    }

    [Fact]
    public async Task ExecuteAsync_FactoryThrows_PropagatesException()
    {
        var personaRepo = PersonaRepoWith(TestPersona);
        var factory = Substitute.For<IChatClientFactory>();
        factory.Create(Arg.Any<BackendId>())
            .Returns(_ => throw new InvalidOperationException("backend not configured"));

        var engine = new ChatClientWorkflowEngine(personaRepo, factory, FixedClock(), NullLogger<ChatClientWorkflowEngine>.Instance);

        var events = new List<RunEvent>();
        var act = async () =>
        {
            await foreach (var e in engine.ExecuteAsync(TestWorkflow, TestInput, Guid.NewGuid(), CancellationToken.None))
            {
                events.Add(e);
            }
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*backend*");
        events.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_PropagatesOperationCanceled()
    {
        var personaRepo = PersonaRepoWith(TestPersona);
        var chatClient = Substitute.For<IChatClient>();
        chatClient.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var ct = call.Arg<CancellationToken>();
                return StreamFrom(new[] { "first", "second", "third" }, ct);
            });

        var factory = Substitute.For<IChatClientFactory>();
        factory.Create(BackendId.AzureOpenAi).Returns(chatClient);

        var engine = new ChatClientWorkflowEngine(personaRepo, factory, FixedClock(), NullLogger<ChatClientWorkflowEngine>.Instance);

        using var cts = new CancellationTokenSource();

        var act = async () =>
        {
            await foreach (var e in engine.ExecuteAsync(TestWorkflow, TestInput, Guid.NewGuid(), cts.Token))
            {
                cts.Cancel();
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_PassesGuardrailsToChatOptions()
    {
        var personaRepo = PersonaRepoWith(TestPersona);
        var chatClient = Substitute.For<IChatClient>();
        ChatOptions? capturedOptions = null;
        chatClient.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Do<ChatOptions?>(o => capturedOptions = o),
                Arg.Any<CancellationToken>())
            .Returns(_ => StreamFrom(new[] { "ok" }));

        var factory = Substitute.For<IChatClientFactory>();
        factory.Create(BackendId.AzureOpenAi).Returns(chatClient);

        var engine = new ChatClientWorkflowEngine(personaRepo, factory, FixedClock(), NullLogger<ChatClientWorkflowEngine>.Instance);

        await foreach (var _ in engine.ExecuteAsync(TestWorkflow, TestInput, Guid.NewGuid(), CancellationToken.None))
        {
        }

        capturedOptions.Should().NotBeNull();
        capturedOptions!.MaxOutputTokens.Should().Be(256);
        capturedOptions.Temperature.Should().BeApproximately(0.2f, 0.001f);
        capturedOptions.TopP.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_NoGuardrails_PassesNullChatOptions()
    {
        var personaRepo = PersonaRepoWith(TestPersona with { Guardrails = Guardrails.Default });
        var chatClient = Substitute.For<IChatClient>();
        ChatOptions? capturedOptions = new ChatOptions { Temperature = 1f };
        chatClient.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Do<ChatOptions?>(o => capturedOptions = o),
                Arg.Any<CancellationToken>())
            .Returns(_ => StreamFrom(new[] { "ok" }));

        var factory = Substitute.For<IChatClientFactory>();
        factory.Create(BackendId.AzureOpenAi).Returns(chatClient);

        var engine = new ChatClientWorkflowEngine(personaRepo, factory, FixedClock(), NullLogger<ChatClientWorkflowEngine>.Instance);

        await foreach (var _ in engine.ExecuteAsync(TestWorkflow, TestInput, Guid.NewGuid(), CancellationToken.None))
        {
        }

        capturedOptions.Should().BeNull();
    }
}
