using System.Runtime.CompilerServices;
using System.Text.Json;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Application.Exceptions;
using CloudEngAgent.Domain.Runs;

namespace CloudEngAgent.Application.Runs;

public sealed class StartWorkflowRunHandler(
    IWorkflowRegistry workflows,
    IWorkflowEngine engine,
    IRunStore runs,
    IClock clock)
{
    public async IAsyncEnumerable<RunEvent> HandleAsync(
        StartWorkflowRunInput input,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var workflow = workflows.Get(input.WorkflowId)
            ?? throw new WorkflowNotFoundException(input.WorkflowId);

        var runId = Guid.NewGuid();
        var run = new Run(
            runId,
            workflow.Id,
            RunStatus.Running,
            clock.UtcNow,
            EndedAt: null,
            InputSummary: Truncate(input.UserInput, 512));

        await runs.CreateAsync(run, cancellationToken).ConfigureAwait(false);

        var started = new RunEvent(
            runId,
            RunEventType.RunStarted,
            JsonSerializer.Serialize(new { input.WorkflowId, input.ThreadId, input.RequestedBy }),
            SequenceNo: 0,
            OccurredAt: clock.UtcNow);
        await runs.AppendEventAsync(started, cancellationToken).ConfigureAwait(false);
        yield return started;

        long seq = 1;
        var terminalStatus = RunStatus.Succeeded;

        IAsyncEnumerable<RunEvent> stream;
        try
        {
            stream = engine.ExecuteAsync(workflow, input, runId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            terminalStatus = RunStatus.Cancelled;
            await FinishAsync(run, terminalStatus, seq, reason: null, cancellationToken).ConfigureAwait(false);
            throw;
        }

        await using var enumerator = stream.GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            RunEvent? next = null;
            Exception? failure = null;
            var done = false;

            try
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    done = true;
                }
                else
                {
                    next = enumerator.Current with { SequenceNo = seq++ };
                }
            }
            catch (OperationCanceledException)
            {
                terminalStatus = RunStatus.Cancelled;
                await FinishAsync(run, terminalStatus, seq, reason: "cancelled", CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            if (failure is not null)
            {
                terminalStatus = RunStatus.Failed;
                var errorEvent = new RunEvent(
                    runId,
                    RunEventType.Error,
                    JsonSerializer.Serialize(new { message = failure.Message, type = failure.GetType().FullName }),
                    SequenceNo: seq++,
                    OccurredAt: clock.UtcNow);
                await runs.AppendEventAsync(errorEvent, CancellationToken.None).ConfigureAwait(false);
                yield return errorEvent;
                await FinishAsync(run, terminalStatus, seq, failure.Message, CancellationToken.None).ConfigureAwait(false);
                yield break;
            }

            if (done)
            {
                break;
            }

            await runs.AppendEventAsync(next!, cancellationToken).ConfigureAwait(false);
            yield return next!;
        }

        await FinishAsync(run, terminalStatus, seq, reason: null, cancellationToken).ConfigureAwait(false);
    }

    private async Task FinishAsync(Run run, RunStatus terminal, long sequence, string? reason, CancellationToken cancellationToken)
    {
        var finished = run.WithStatus(terminal, clock.UtcNow);
        await runs.UpdateAsync(finished, cancellationToken).ConfigureAwait(false);

        var finishedEvent = new RunEvent(
            run.Id,
            RunEventType.RunFinished,
            JsonSerializer.Serialize(new { status = terminal.ToString(), reason }),
            SequenceNo: sequence,
            OccurredAt: clock.UtcNow);
        await runs.AppendEventAsync(finishedEvent, cancellationToken).ConfigureAwait(false);
    }

    private static string? Truncate(string? value, int max)
    {
        if (value is null)
        {
            return null;
        }

        return value.Length <= max ? value : value[..max];
    }
}
