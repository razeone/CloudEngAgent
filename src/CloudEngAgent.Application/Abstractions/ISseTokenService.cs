namespace CloudEngAgent.Application.Abstractions;

/// <summary>
/// Issues and validates short-lived tokens that authorize a single SSE stream
/// for a specific <c>runId</c>. The token is passed as a query-string parameter
/// because EventSource clients cannot set Authorization headers.
/// </summary>
public interface ISseTokenService
{
    /// <summary>
    /// Issues a token bound to <paramref name="runId"/> and <paramref name="subject"/>
    /// that expires after <see cref="TokenLifetime"/>.
    /// </summary>
    SseTokenIssued Issue(Guid runId, string subject);

    /// <summary>
    /// Returns true if <paramref name="token"/> is well-formed, unexpired, and
    /// bound to <paramref name="runId"/>. <paramref name="subject"/> receives
    /// the original subject claim on success.
    /// </summary>
    bool TryValidate(string? token, Guid runId, out string? subject);

    /// <summary>How long a freshly-issued token remains valid.</summary>
    TimeSpan TokenLifetime { get; }
}

public sealed record SseTokenIssued(string Token, DateTimeOffset ExpiresAt);
