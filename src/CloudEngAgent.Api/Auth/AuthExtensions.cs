using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Identity.Web;

namespace CloudEngAgent.Api.Auth;

internal static class AuthExtensions
{
    public const string DevelopmentBypassScheme = "DevelopmentBypass";

    public static IServiceCollection AddCloudEngAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var entra = configuration.GetSection("Entra");
        var tenantConfigured = !string.IsNullOrWhiteSpace(entra["TenantId"]);

        if (tenantConfigured)
        {
            services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApi(entra);
        }
        else
        {
            // Strict whitelist: only the literal "Development" environment may
            // run without Entra. Anything else (Staging, Production, custom) refuses to start.
            if (!string.Equals(environment.EnvironmentName, Environments.Development, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Entra:TenantId is not configured and environment is '{environment.EnvironmentName}'. " +
                    "Refusing to start outside of Development without authentication.");
            }

            services
                .AddAuthentication(DevelopmentBypassScheme)
                .AddScheme<DevelopmentBypassOptions, DevelopmentBypassHandler>(DevelopmentBypassScheme, _ => { });
        }

        services.AddAuthorization();
        return services;
    }
}
