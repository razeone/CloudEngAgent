using System.Text;
using System.Text.Json;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Domain.Artifacts;
using CloudEngAgent.Domain.Inputs;
using CloudEngAgent.Domain.Widgets;
using Microsoft.Extensions.Logging;

namespace CloudEngAgent.Infrastructure.Widgets;

/// <summary>
/// Validation- and policy-enforcing implementation of <see cref="IUiSurface"/>.
///
/// <para>
/// Every <see cref="EmitSnapshotAsync{TProps}"/> call is gated by:
/// (1) <see cref="IWidgetRegistry.IsAllowed"/> against the surface's persona,
/// (2) the per-type <see cref="WidgetPolicy.MaxPayloadBytes"/> cap, and
/// (3) the per-type JSON Schema. Any failure throws a
/// <see cref="WidgetEmitException"/> so the workflow node sees a deterministic
/// error instead of a malformed widget reaching the AG-UI stream.
/// </para>
///
/// <para>
/// Persistence (event log + materialized widget state) lives in the storage
/// worktree's EF-backed implementation; this class stays storage-agnostic so
/// the widget validation surface can be unit-tested without a database. The
/// storage worktree wires this class behind a decorator that writes the
/// returned <see cref="WidgetState"/> in the same EF transaction as the
/// run-event log entry.
/// </para>
/// </summary>
public sealed class UiSurface : IUiSurface
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly IWidgetRegistry _registry;
    private readonly string _personaId;
    private readonly ILogger<UiSurface> _logger;

    public UiSurface(IWidgetRegistry registry, string personaId, ILogger<UiSurface> logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(personaId);
        ArgumentNullException.ThrowIfNull(logger);

        _registry = registry;
        _personaId = personaId;
        _logger = logger;
    }

    public Task<WidgetState> EmitSnapshotAsync<TProps>(
        WidgetKey key,
        WidgetType type,
        WidgetStatus status,
        WidgetPlacement placement,
        TProps props,
        IReadOnlyList<ArtifactRef>? artifacts = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(props);

        if (!_registry.IsAllowed(_personaId, type))
        {
            throw new WidgetEmitException(
                $"Persona '{_personaId}' is not allowed to emit widget type '{type.Value}'.");
        }

        var policy = _registry.GetPolicy(type);
        var propsJson = JsonSerializer.Serialize(props, SerializerOptions);
        var sizeBytes = Encoding.UTF8.GetByteCount(propsJson);
        if (sizeBytes > policy.MaxPayloadBytes)
        {
            throw new WidgetEmitException(
                $"Widget '{type.Value}' payload is {sizeBytes} bytes, exceeds policy max "
                + $"{policy.MaxPayloadBytes} bytes.");
        }

        var validation = _registry.Validate(type, propsJson);
        if (!validation.IsValid)
        {
            throw new WidgetEmitException(
                $"Widget '{type.Value}' props failed schema validation: "
                + string.Join("; ", validation.Errors));
        }

        var state = new WidgetState(
            Key: key,
            Type: type,
            Status: status,
            Revision: 1,
            Placement: placement,
            PropsJson: propsJson,
            Artifacts: artifacts ?? Array.Empty<ArtifactRef>());

        _logger.LogDebug(
            "Emitted widget snapshot {Key} type={Type} bytes={Bytes}",
            key.ToCanonicalString(), type.Value, sizeBytes);

        return Task.FromResult(state);
    }

    /// <summary>
    /// Patch application requires the prior <see cref="WidgetState"/> to compute
    /// the new revision and re-validate the resulting props. Both reads and
    /// writes against materialized state live in the storage worktree's EF
    /// implementation; this in-memory surface intentionally does not implement
    /// it to avoid a partial, lossy patch path.
    /// </summary>
    public Task<WidgetState> PatchAsync(
        WidgetKey key,
        IReadOnlyList<JsonPatchOp> patches,
        WidgetStatus? newStatus = null,
        CancellationToken ct = default) =>
        throw new NotSupportedException(
            "PatchAsync requires materialized widget state; provide a storage-backed IUiSurface.");

    /// <summary>
    /// Input requests are persisted in the input-request store owned by the
    /// storage worktree. See <see cref="UiSurface"/> remarks.
    /// </summary>
    public Task<InputRequest> RequestInputAsync(
        WidgetKey approvalCardKey,
        string schemaRef,
        TimeSpan timeout,
        CancellationToken ct = default) =>
        throw new NotSupportedException(
            "RequestInputAsync requires the input-request store; provide a storage-backed IUiSurface.");

    /// <summary>
    /// Artifacts are written to the artifact store owned by the storage
    /// worktree. See <see cref="UiSurface"/> remarks.
    /// </summary>
    public Task<ArtifactRef> EmitArtifactAsync(
        Artifact artifact,
        CancellationToken ct = default) =>
        throw new NotSupportedException(
            "EmitArtifactAsync requires the artifact store; provide a storage-backed IUiSurface.");
}

/// <summary>
/// Thrown when an <see cref="IUiSurface"/> emit is rejected by the registry —
/// disallowed widget, oversized payload, or schema-invalid props.
/// </summary>
public sealed class WidgetEmitException : Exception
{
    public WidgetEmitException(string message) : base(message) { }

    public WidgetEmitException(string message, Exception innerException)
        : base(message, innerException) { }
}
