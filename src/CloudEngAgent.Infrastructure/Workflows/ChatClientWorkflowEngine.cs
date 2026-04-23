using System.Runtime.CompilerServices;
using System.Text.Json;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Application.Runs;
using CloudEngAgent.Domain.Personas;
using CloudEngAgent.Domain.Runs;
using CloudEngAgent.Domain.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CloudEngAgent.Infrastructure.Workflows;

/// <summary>
/// Real <see cref="IWorkflowEngine"/> that drives the entry persona's
/// <see cref="IChatClient"/> for a single-agent streaming response.
///
/// This is the M5.1 slice — multi-agent graph orchestration via
/// <c>Microsoft.Agents.AI.Workflows</c> is the M5.2 follow-up. The engine
/// emits an <see cref="RunEventType.AgentHandoff"/> on start, then
/// <see cref="RunEventType.TextDelta"/> events for each streamed chunk.
/// </summary>
public sealed class ChatClientWorkflowEngine(
    IPersonaRepository personas,
    IChatClientFactory chatClientFactory,
    IClock clock,
    ILogger<ChatClientWorkflowEngine> logger) : IWorkflowEngine
{
    public async IAsyncEnumerable<RunEvent> ExecuteAsync(
        WorkflowDefinition workflow,
        StartWorkflowRunInput input,
        Guid runId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(input);

        var persona = await personas.GetAsync(workflow.EntryPersonaId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Workflow '{workflow.Id}' references unknown entry persona '{workflow.EntryPersonaId}'.");

        // Single-agent slice: entry persona handles the whole turn. Multi-agent
        // routing lands with the AI.Workflows graph in M5.2.
        yield return MakeEvent(runId, RunEventType.AgentHandoff, new
        {
            from = (string?)null,
            to = persona.Id,
            reason = "entry",
        });

        var chatClient = chatClientFactory.Create(persona.Backend);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, persona.SystemPrompt),
            new(ChatRole.User, input.UserInput ?? string.Empty),
        };

        var options = ToChatOptions(persona.Guardrails);
        var messageId = Guid.NewGuid().ToString("n");

        IAsyncEnumerable<ChatResponseUpdate> stream;
        try
        {
            stream = chatClient.GetStreamingResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to start streaming response for persona {PersonaId}", persona.Id);
            throw;
        }

        await foreach (var update in stream.ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = ExtractText(update);
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            yield return MakeEvent(runId, RunEventType.TextDelta, new
            {
                messageId,
                role = "assistant",
                delta = text,
                agentId = persona.Id,
            });
        }
    }

    private static ChatOptions? ToChatOptions(Guardrails guardrails)
    {
        if (guardrails.MaxTokens is null && guardrails.Temperature is null && guardrails.TopP is null)
        {
            return null;
        }

        return new ChatOptions
        {
            MaxOutputTokens = guardrails.MaxTokens,
            Temperature = (float?)guardrails.Temperature,
            TopP = (float?)guardrails.TopP,
        };
    }

    private static string ExtractText(ChatResponseUpdate update)
    {
        if (update.Contents is null || update.Contents.Count == 0)
        {
            return update.Text ?? string.Empty;
        }

        // Concatenate any TextContent parts in this update.
        var sb = new System.Text.StringBuilder();
        foreach (var content in update.Contents)
        {
            if (content is TextContent text)
            {
                sb.Append(text.Text);
            }
        }
        return sb.Length > 0 ? sb.ToString() : update.Text ?? string.Empty;
    }

    private RunEvent MakeEvent(Guid runId, RunEventType type, object payload) => new(
        RunId: runId,
        Type: type,
        PayloadJson: JsonSerializer.Serialize(payload),
        SequenceNo: 0,
        OccurredAt: clock.UtcNow);
}
