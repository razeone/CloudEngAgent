using CloudEngAgent.Domain.Artifacts;
using CloudEngAgent.Domain.Inputs;
using CloudEngAgent.Domain.Widgets;

namespace CloudEngAgent.Application.Abstractions;

/// <summary>
/// The agent-facing surface for emitting generative-UI widgets, applying
/// incremental patches, requesting user input, and recording artifacts.
/// Implementations are responsible for validating props via
/// <see cref="IWidgetRegistry"/> and persisting both the run-event log entry
/// and the materialized widget state in a single transaction.
/// </summary>
public interface IUiSurface
{
    /// <summary>Emits a full snapshot of a widget. Validates props via IWidgetRegistry.</summary>
    Task<WidgetState> EmitSnapshotAsync<TProps>(
        WidgetKey key,
        WidgetType type,
        WidgetStatus status,
        WidgetPlacement placement,
        TProps props,
        IReadOnlyList<ArtifactRef>? artifacts = null,
        CancellationToken ct = default);

    /// <summary>Applies an RFC 6902 patch to an existing widget's props. Increments revision.</summary>
    Task<WidgetState> PatchAsync(
        WidgetKey key,
        IReadOnlyList<JsonPatchOp> patches,
        WidgetStatus? newStatus = null,
        CancellationToken ct = default);

    /// <summary>Requests user input. Workflow node should await the returned Task.</summary>
    Task<InputRequest> RequestInputAsync(
        WidgetKey approvalCardKey,
        string schemaRef,
        TimeSpan timeout,
        CancellationToken ct = default);

    /// <summary>Records an artifact (already persisted to artifact store). Returns ref for embedding in widget props.</summary>
    Task<ArtifactRef> EmitArtifactAsync(
        Artifact artifact,
        CancellationToken ct = default);
}
