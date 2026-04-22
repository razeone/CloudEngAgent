using System.Runtime.CompilerServices;
using System.Text.Json;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Application.Runs;
using CloudEngAgent.Domain.Runs;
using CloudEngAgent.Domain.Workflows;

namespace CloudEngAgent.Infrastructure.Workflows;

/// <summary>
/// Deterministic stub <see cref="IWorkflowEngine"/> used by the API milestone.
/// Emits a canonical event sequence so the AG-UI streaming layer can be
/// exercised end-to-end without an LLM key. Replaced by a real engine that
/// drives <c>Microsoft.Agents.AI.Workflows</c> in a later plan.
/// </summary>
public sealed class StubWorkflowEngine(IClock clock) : IWorkflowEngine
{
    private readonly IClock _clock = clock;

    public async IAsyncEnumerable<RunEvent> ExecuteAsync(
        WorkflowDefinition workflow,
        StartWorkflowRunInput input,
        Guid runId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(input);

        var entry = workflow.EntryPersonaId;
        var next = workflow.PersonaIds.FirstOrDefault(p => p != entry) ?? entry;

        yield return MakeEvent(runId, RunEventType.AgentHandoff, new
        {
            from = entry,
            to = next,
            reason = "stub-routing",
        });

        var messageId = Guid.NewGuid().ToString("n");
        var pieces = new[]
        {
            $"Acknowledged request on workflow '{workflow.Id}'. ",
            $"Routing to persona '{next}'. ",
            "This is a stub response from the API scaffolding.",
        };

        foreach (var piece in pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return MakeEvent(runId, RunEventType.TextDelta, new
            {
                messageId,
                role = "assistant",
                delta = piece,
                agentId = next,
            });
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }

        var toolCallId = Guid.NewGuid().ToString("n");
        yield return MakeEvent(runId, RunEventType.ToolCall, new
        {
            toolCallId,
            toolName = "mcp:cloudeng-db.list_databases",
            arguments = "{}",
            agentId = next,
        });
        yield return MakeEvent(runId, RunEventType.ToolResult, new
        {
            toolCallId,
            isError = false,
            content = "[]",
        });
    }

    private RunEvent MakeEvent(Guid runId, RunEventType type, object payload) => new(
        RunId: runId,
        Type: type,
        PayloadJson: JsonSerializer.Serialize(payload),
        SequenceNo: 0,
        OccurredAt: _clock.UtcNow);
}
