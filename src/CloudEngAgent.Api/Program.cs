using System.Threading.RateLimiting;
using CloudEngAgent.Api.Auth;
using CloudEngAgent.Api.Configuration;
using CloudEngAgent.Api.Endpoints;
using CloudEngAgent.Api.Middleware;
using CloudEngAgent.Api.Observability;
using CloudEngAgent.Api.Runs;
using CloudEngAgent.Application.Exceptions;
using CloudEngAgent.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// ---- Strongly-typed options with startup validation ----
builder.Services.AddOptions<CorsOptions>()
    .Bind(builder.Configuration.GetSection(CorsOptions.Section))
    .Validate(o => builder.Environment.IsDevelopment() || (o.AllowedOrigins is { Length: > 0 }),
        "Cors:AllowedOrigins must be configured in non-Development environments.")
    .ValidateOnStart();
builder.Services.AddOptions<RunsOptions>()
    .Bind(builder.Configuration.GetSection(RunsOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<SseOptions>()
    .Bind(builder.Configuration.GetSection(SseOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<EntraOptions>()
    .Bind(builder.Configuration.GetSection(EntraOptions.Section))
    .ValidateOnStart();
builder.Services.AddOptions<OpenTelemetryOptions>()
    .Bind(builder.Configuration.GetSection(OpenTelemetryOptions.Section))
    .ValidateOnStart();
builder.Services.AddOptions<RateLimitOptions>()
    .Bind(builder.Configuration.GetSection(RateLimitOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails(opts =>
{
    opts.CustomizeProblemDetails = ctx =>
    {
        if (ctx.HttpContext.Items.TryGetValue("RequestId", out var rid) && rid is string s)
        {
            ctx.ProblemDetails.Extensions["requestId"] = s;
        }
    };
});

var corsOptions = builder.Configuration.GetSection(CorsOptions.Section).Get<CorsOptions>() ?? new CorsOptions();
builder.Services.AddCors(options => options.AddPolicy("frontend", policy =>
{
    if (corsOptions.AllowedOrigins.Length == 0)
    {
        policy.SetIsOriginAllowed(_ => false);
    }
    else
    {
        policy
            .WithOrigins(corsOptions.AllowedOrigins)
            .WithHeaders("Authorization", "Content-Type", "Last-Event-ID", "X-Request-Id")
            .WithMethods("GET", "POST", "OPTIONS")
            .AllowCredentials();
    }
}));

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddSingleton<RunDispatcher>();
builder.Services.AddCloudEngAuth(builder.Configuration, builder.Environment);

// ---- Rate limiting ----
var rateLimit = builder.Configuration.GetSection(RateLimitOptions.Section).Get<RateLimitOptions>() ?? new RateLimitOptions();
if (rateLimit.Enabled)
{
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("write", httpCtx => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpCtx.User?.Identity?.Name ?? httpCtx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = rateLimit.WritePermitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
        options.AddPolicy("read", httpCtx => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpCtx.User?.Identity?.Name ?? httpCtx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = rateLimit.ReadPermitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
    });
}

// ---- OpenTelemetry ----
var otel = builder.Configuration.GetSection(OpenTelemetryOptions.Section).Get<OpenTelemetryOptions>() ?? new OpenTelemetryOptions();
if (otel.Enabled)
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(otel.ServiceName))
        .WithTracing(t =>
        {
            t.AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource(Telemetry.ActivitySourceName);
            if (!string.IsNullOrWhiteSpace(otel.OtlpEndpoint))
            {
                t.AddOtlpExporter(o => o.Endpoint = new Uri(otel.OtlpEndpoint));
            }
            else
            {
                t.AddOtlpExporter();
            }
        })
        .WithMetrics(m =>
        {
            m.AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(Telemetry.MeterName);
            if (!string.IsNullOrWhiteSpace(otel.OtlpEndpoint))
            {
                m.AddOtlpExporter(o => o.Endpoint = new Uri(otel.OtlpEndpoint));
            }
            else
            {
                m.AddOtlpExporter();
            }
        });
}

var app = builder.Build();

app.UseExceptionHandler(eb => eb.Run(async context =>
{
    var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
    var ex = feature?.Error;

    var (status, title) = ex switch
    {
        WorkflowNotFoundException => (StatusCodes.Status404NotFound, ex!.Message),
        OperationCanceledException => (499, "Client cancelled the request"),
        _ => (StatusCodes.Status500InternalServerError, "Unexpected error"),
    };

    context.Response.StatusCode = status;
    await Results.Problem(title: title, statusCode: status).ExecuteAsync(context);
}));

app.UseMiddleware<RequestIdMiddleware>();
app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();
if (rateLimit.Enabled)
{
    app.UseRateLimiter();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapHealthChecks("/readyz").AllowAnonymous();

app.MapV1();

app.Run();

// Exposed so WebApplicationFactory<Program> can find the entry assembly.
public partial class Program;
