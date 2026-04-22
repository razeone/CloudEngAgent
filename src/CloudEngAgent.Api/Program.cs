using CloudEngAgent.Api.Auth;
using CloudEngAgent.Api.Endpoints;
using CloudEngAgent.Api.Runs;
using CloudEngAgent.Application.Exceptions;
using CloudEngAgent.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options => options.AddPolicy("frontend", policy =>
{
    if (allowedOrigins.Length == 0)
    {
        policy.SetIsOriginAllowed(_ => false);
    }
    else
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    }
}));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<RunDispatcher>();
builder.Services.AddCloudEngAuth(builder.Configuration, builder.Environment);

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

app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapGet("/readyz", () => Results.Ok(new { status = "ready" })).AllowAnonymous();

app.MapV1();

app.Run();

// Exposed so WebApplicationFactory<Program> can find the entry assembly.
public partial class Program;
