# CloudEngAgent

A multi-agent .NET 10 backend that empowers DBAs and power users with AI-driven exploration, analysis, and administration of SQL databases. Connects to multiple LLM backends and exposes operational tools through MCP (Model Context Protocol). Streams results to clients over an [AG-UI](https://docs.ag-ui.com/) compatible API.

## Status

Early development. The HTTP + AG-UI streaming layer is in place with in-memory stubs for the workflow engine, persona repository, and run store. Real EF Core persistence, real LLM/MCP backends, and the multi-agent workflow graph land in milestones M2–M7 (see `session-state/.../plan.md`).

## Solution layout

```
src/
  CloudEngAgent.Domain/         Pure domain types (Run, RunEvent, Persona, Workflow)
  CloudEngAgent.Application/    Use-cases, abstractions, handlers (no I/O)
  CloudEngAgent.Infrastructure/ In-memory adapters + DI composition
  CloudEngAgent.Api/            ASP.NET Core minimal API + AG-UI SSE writer
  CloudEngAgent.Mcp.Server/     MCP server (skeleton; M6)
tests/
  CloudEngAgent.Domain.Tests/
  CloudEngAgent.Api.Tests/
```

## Run locally

```powershell
dotnet build
dotnet test
dotnet run --project src/CloudEngAgent.Api
```

The API listens on `http://localhost:5xxx` (kestrel-assigned). Health probes:

```
GET /healthz
GET /readyz
```

OpenAPI docs are exposed at `/openapi/v1.json` in Development.

## API surface (v1)

| Method | Route                                  | Description                                              |
| ------ | -------------------------------------- | -------------------------------------------------------- |
| GET    | `/v1/workflows`                        | List registered workflows                                |
| GET    | `/v1/workflows/{id}`                   | Get a workflow by id                                     |
| GET    | `/v1/personas`                         | List personas                                            |
| GET    | `/v1/personas/{id}`                    | Get a persona by id                                      |
| POST   | `/v1/runs`                             | Start a workflow run                                     |
| POST   | `/v1/runs/{runId}/cancel`              | Cancel an in-flight run                                  |
| POST   | `/v1/runs/{runId}/sse-token`           | Mint a short-lived token for the SSE stream              |
| GET    | `/v1/runs/{runId}/events?token=...`    | AG-UI Server-Sent Events stream (token required)         |

### AG-UI event mapping

The AG-UI writer translates internal `RunEvent` types into AG-UI SSE events:

| `RunEventType`     | AG-UI event                                            |
| ------------------ | ------------------------------------------------------ |
| `RunStarted`       | `RUN_STARTED`                                          |
| `RunFinished`      | `RUN_FINISHED`                                         |
| `Error`            | `RUN_ERROR`                                            |
| `MessageStart`     | `TEXT_MESSAGE_START`                                   |
| `TextDelta`        | `TEXT_MESSAGE_CONTENT`                                 |
| `MessageEnd`       | `TEXT_MESSAGE_END`                                     |
| `ToolCall`         | `TOOL_CALL_START` + `TOOL_CALL_ARGS`                   |
| `ToolResult`       | Custom `tool_result`                                   |
| `AgentHandoff`     | `STEP_FINISHED` (current) + `STEP_STARTED` (next)      |
| `StateSnapshot`    | `STATE_SNAPSHOT`                                       |

### SSE security model

The SSE endpoint is protected by a short-lived token bound to `(runId, subjectId, exp)`:

1. The client authenticates normally (Entra in production, dev-bypass in `Development`) and POSTs to `/v1/runs/{runId}/sse-token`.
2. The server returns a `Microsoft.AspNetCore.DataProtection`-protected token (default 120 s lifetime, clamped 30–600 s).
3. The client opens `GET /v1/runs/{runId}/events?token=<token>`.
4. The endpoint validates the token (signature, expiry, runId binding) before opening the stream.

This avoids leaking SSE streams to anonymous clients while keeping the EventSource API simple (no custom headers).

## Configuration

`appsettings.json` highlights:

```jsonc
{
  "Cors":          { "AllowedOrigins": ["http://localhost:5173"] },
  "Runs":          { "MaxConcurrent": 32, "EventBufferSize": 1024 },
  "Sse":           { "TokenLifetimeSeconds": 120 },
  "RateLimiting":  { "Enabled": true, "WritePermitsPerMinute": 60, "ReadPermitsPerMinute": 600 },
  "OpenTelemetry": { "Enabled": false, "ServiceName": "CloudEngAgent.Api", "OtlpEndpoint": "" },
  "Entra":         { "TenantId": "", "Audience": "api://cloud-eng-agent" },
  "Backends":      { /* per-backend connection settings */ },
  "Mcp":           { "Servers": [ /* MCP servers */ ] },
  "Personas":      { "Path": "./personas" }
}
```

In non-Development environments the app fails fast at startup if `Cors:AllowedOrigins` is empty.

## Container

```powershell
docker build -t cloudeng-agent-api -f src/CloudEngAgent.Api/Dockerfile .
docker run --rm -p 8080:8080 cloudeng-agent-api
```

The container runs as non-root and exposes a `HEALTHCHECK` against `/healthz`.

## Roadmap

See the in-session plan for the full P0/P1/P2 backlog and milestones M2 (EF Core), M3 (real LLM backends), M4 (YAML personas + hot reload), M5 (real workflow engine on `Microsoft.Agents.AI.Workflows`), M6 (MCP SQL server), M7 (MCP client wiring). Forward-looking features (P3) include resumption, multi-tenancy, persona overlays, a workflow DSL, human-in-the-loop tool approval, cost/token telemetry, and saved investigations.
