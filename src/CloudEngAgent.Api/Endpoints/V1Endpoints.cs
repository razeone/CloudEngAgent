using CloudEngAgent.Api.Contracts;
using CloudEngAgent.Api.Runs;
using CloudEngAgent.Api.Sse;
using CloudEngAgent.Application.Abstractions;
using CloudEngAgent.Application.Runs;
using CloudEngAgent.Domain.Runs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CloudEngAgent.Api.Endpoints;

internal static class V1Endpoints
{
    public static IEndpointRouteBuilder MapV1(this IEndpointRouteBuilder routes)
    {
        var v1 = routes.MapGroup("/v1").RequireAuthorization();

        MapWorkflows(v1);
        MapPersonas(v1);
        MapRuns(v1);

        return routes;
    }

    private static void MapWorkflows(RouteGroupBuilder v1)
    {
        var group = v1.MapGroup("/workflows").WithTags("workflows").RequireRateLimiting("read");

        group.MapGet("/", (IWorkflowRegistry registry) =>
            Results.Ok(registry.List().Select(WorkflowDto.FromDomain).ToArray()));

        group.MapGet("/{id}", (string id, IWorkflowRegistry registry) =>
        {
            var w = registry.Get(id);
            return w is null ? Results.NotFound() : Results.Ok(WorkflowDto.FromDomain(w));
        });
    }

    private static void MapPersonas(RouteGroupBuilder v1)
    {
        var group = v1.MapGroup("/personas").WithTags("personas").RequireRateLimiting("read");

        group.MapGet("/", async (IPersonaRepository repo, CancellationToken ct) =>
        {
            var list = new List<PersonaDto>();
            await foreach (var p in repo.ListAsync(ct).ConfigureAwait(false))
            {
                list.Add(PersonaDto.FromDomain(p));
            }

            return Results.Ok(list);
        });

        group.MapGet("/{id}", async (string id, IPersonaRepository repo, CancellationToken ct) =>
        {
            var p = await repo.GetAsync(id, ct).ConfigureAwait(false);
            return p is null ? Results.NotFound() : Results.Ok(PersonaDto.FromDomain(p));
        });
    }

    private static void MapRuns(RouteGroupBuilder v1)
    {
        var group = v1.MapGroup("/runs").WithTags("runs");

        group.MapPost("/", async (StartRunRequest request, RunDispatcher dispatcher, CancellationToken ct) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.WorkflowId) || string.IsNullOrWhiteSpace(request.UserInput))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "workflowId and userInput are required");
            }

            try
            {
                var runId = await dispatcher.StartAsync(request, ct).ConfigureAwait(false);
                var response = new StartRunResponse(runId, RunStatus.Running.ToString(), $"/v1/runs/{runId}/events");
                return Results.Accepted($"/v1/runs/{runId}", response);
            }
            catch (Application.Exceptions.WorkflowNotFoundException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: ex.Message);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("concurrency", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: ex.Message);
            }
        }).RequireRateLimiting("write");

        group.MapGet("/", async (string? workflowId, RunStatus? status, int? take, int? skip, IRunStore store, CancellationToken ct) =>
        {
            var query = new ListRunsQuery(workflowId, status, take ?? 50, skip ?? 0);
            var list = new List<RunDto>();
            await foreach (var r in store.ListAsync(query, ct).ConfigureAwait(false))
            {
                list.Add(RunDto.FromDomain(r));
            }

            return Results.Ok(list);
        });

        group.MapGet("/{runId:guid}", async (Guid runId, IRunStore store, CancellationToken ct) =>
        {
            var run = await store.GetAsync(runId, ct).ConfigureAwait(false);
            return run is null ? Results.NotFound() : Results.Ok(RunDto.FromDomain(run));
        });

        group.MapGet("/{runId:guid}/messages", async (Guid runId, IRunStore store, CancellationToken ct) =>
        {
            var list = new List<MessageDto>();
            await foreach (var m in store.GetMessagesAsync(runId, ct).ConfigureAwait(false))
            {
                list.Add(MessageDto.FromDomain(m));
            }

            return Results.Ok(list);
        });

        group.MapPost("/{runId:guid}/cancel", (Guid runId, RunDispatcher dispatcher) =>
            dispatcher.TryCancel(runId) ? Results.Accepted() : Results.NotFound())
            .RequireRateLimiting("write");

        group.MapPost("/{runId:guid}/sse-token", async (Guid runId, HttpContext ctx, IRunStore store, ISseTokenService tokens, CancellationToken ct) =>
        {
            var run = await store.GetAsync(runId, ct).ConfigureAwait(false);
            if (run is null)
            {
                return Results.NotFound();
            }

            var subject = ctx.User?.Identity?.Name ?? "anonymous";
            var issued = tokens.Issue(runId, subject);
            var url = $"/v1/runs/{runId}/events?token={Uri.EscapeDataString(issued.Token)}";
            return Results.Ok(new SseTokenResponse(
                Token: issued.Token,
                EventsUrl: url,
                ExpiresInSeconds: (int)tokens.TokenLifetime.TotalSeconds,
                ExpiresAt: issued.ExpiresAt));
        }).RequireRateLimiting("write");

        group.MapGet("/{runId:guid}/events",
            [AllowAnonymous] async (Guid runId, HttpContext ctx, IRunStore store, IRunEventBus bus, ISseTokenService tokens, ILoggerFactory loggerFactory) =>
            {
                // EventSource clients cannot send Authorization headers; auth is enforced
                // via a short-lived data-protection-signed token issued by POST /sse-token.
                var token = ctx.Request.Query["token"].ToString();
                if (!tokens.TryValidate(token, runId, out _))
                {
                    return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Missing or invalid SSE token");
                }

                var logger = loggerFactory.CreateLogger("CloudEngAgent.Api.Sse");
                await AgUiSseWriter.WriteAsync(ctx, runId, store, bus, logger, ctx.RequestAborted).ConfigureAwait(false);
                return Results.Empty;
            });
    }
}
