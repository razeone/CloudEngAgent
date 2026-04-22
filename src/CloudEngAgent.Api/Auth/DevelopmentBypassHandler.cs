using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Hosting;
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
    UrlEncoder encoder,
    IHostEnvironment hostEnvironment)
    : AuthenticationHandler<DevelopmentBypassOptions>(options, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Defense in depth: even though AddCloudEngAuth refuses to register this
        // scheme outside Development, re-check at request time so a future refactor
        // can't accidentally activate the bypass in a hardened environment.
        if (!string.Equals(hostEnvironment.EnvironmentName, Environments.Development, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.Fail(
                "DevelopmentBypass scheme cannot authenticate outside of the Development environment."));
        }

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
