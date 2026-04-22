using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloudEngAgent.Api.Auth;

/// <summary>
/// Development-only authentication scheme. When <c>Entra:TenantId</c> is not
/// configured and the host environment is <c>Development</c>, every request is
/// authenticated as the anonymous local developer so endpoint authorization
/// still runs the same code path. Refuses to load in non-Development.
/// </summary>
internal sealed class DevelopmentBypassOptions : AuthenticationSchemeOptions;

internal sealed class DevelopmentBypassHandler(
    IOptionsMonitor<DevelopmentBypassOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder)
    : AuthenticationHandler<DevelopmentBypassOptions>(options, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.Name, "local-dev"),
                new Claim(ClaimTypes.NameIdentifier, "local-dev"),
            },
            AuthExtensions.DevelopmentBypassScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
