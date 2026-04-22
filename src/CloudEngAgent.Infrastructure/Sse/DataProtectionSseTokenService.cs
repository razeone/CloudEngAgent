using System.Globalization;
using CloudEngAgent.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace CloudEngAgent.Infrastructure.Sse;

/// <summary>
/// SSE token service backed by ASP.NET Core Data Protection. Tokens are
/// authenticated-encrypted blobs containing <c>runId|exp(unix)|subject</c>.
/// Key rotation, expiration, and tamper detection are handled by the platform.
/// </summary>
public sealed class DataProtectionSseTokenService : ISseTokenService
{
    private const string Purpose = "CloudEngAgent.Sse.v1";

    private readonly IDataProtector _protector;
    private readonly IClock _clock;

    public DataProtectionSseTokenService(IDataProtectionProvider provider, IClock clock, TimeSpan tokenLifetime)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(clock);
        if (tokenLifetime <= TimeSpan.Zero || tokenLifetime > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(nameof(tokenLifetime), "Token lifetime must be >0 and <=10 minutes.");
        }

        _protector = provider.CreateProtector(Purpose);
        _clock = clock;
        TokenLifetime = tokenLifetime;
    }

    public TimeSpan TokenLifetime { get; }

    public SseTokenIssued Issue(Guid runId, string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        var expiresAt = _clock.UtcNow.Add(TokenLifetime);
        var payload = string.Create(CultureInfo.InvariantCulture, $"{runId:N}|{expiresAt.ToUnixTimeSeconds()}|{subject}");
        var token = _protector.Protect(payload);
        return new SseTokenIssued(token, expiresAt);
    }

    public bool TryValidate(string? token, Guid runId, out string? subject)
    {
        subject = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        string payload;
        try
        {
            payload = _protector.Unprotect(token);
        }
        catch
        {
            return false;
        }

        var parts = payload.Split('|', 3);
        if (parts.Length != 3)
        {
            return false;
        }

        if (!Guid.TryParseExact(parts[0], "N", out var tokenRunId) || tokenRunId != runId)
        {
            return false;
        }

        if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expUnix))
        {
            return false;
        }

        if (DateTimeOffset.FromUnixTimeSeconds(expUnix) <= _clock.UtcNow)
        {
            return false;
        }

        subject = parts[2];
        return true;
    }
}
