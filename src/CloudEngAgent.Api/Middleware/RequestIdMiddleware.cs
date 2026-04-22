using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CloudEngAgent.Api.Middleware;

/// <summary>
/// Reads or generates a per-request correlation id, copies it into a response
/// header, and pushes it into the <see cref="ILogger"/> scope so every log line
/// emitted during the request includes the id.
/// </summary>
public sealed class RequestIdMiddleware(RequestDelegate next, ILogger<RequestIdMiddleware> logger)
{
    private const string HeaderName = "X-Request-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers.TryGetValue(HeaderName, out var existing) && !string.IsNullOrWhiteSpace(existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString("N");

        context.Response.Headers[HeaderName] = id;
        context.Items["RequestId"] = id;

        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestId"] = id,
            ["TraceId"] = context.TraceIdentifier,
        }))
        {
            await next(context).ConfigureAwait(false);
        }
    }
}
