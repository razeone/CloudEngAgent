# Architecture overview

This document gives contributors a mental model of the CloudEngAgent
codebase: how the projects relate, where the seams are, and how a request
flows end-to-end. For operational/deployment information see the top-level
[`README.md`](../README.md).

## High-level picture

```
┌─────────────┐    HTTP + AG-UI SSE     ┌──────────────────────────┐
│   Client    │ ──────────────────────► │  CloudEngAgent.Api       │
│  (UI/CLI)   │ ◄──────────────────────  │  (ASP.NET Core 10)       │
└─────────────┘                          └──────────┬───────────────┘
                                                    │ DI
                                                    ▼
                              ┌──────────────────────────────────────┐
                              │  CloudEngAgent.Application           │
                              │  (handlers, abstractions, no I/O)    │
                              └──────────┬─────────────┬─────────────┘
                                         │             │
                                         ▼             ▼
                ┌────────────────────────────┐   ┌──────────────────────┐
                │ CloudEngAgent.Domain       │   │ CloudEngAgent.       │
                │ (pure types, invariants)   │   │ Infrastructure       │
                └────────────────────────────┘   │ (EF Core, MCP HTTP,  │
                                                 │  secrets, persona    │
                                                 │  YAML, chat clients) │
                                                 └────┬────────┬────────┘
                                                      │        │
                                       SQL Server ◄───┘        └──► LLM backends
                                                                    + MCP servers
                                                                       │
                                                                       ▼
                                                  ┌────────────────────────────┐
                                                  │  CloudEngAgent.Mcp.Server  │
                                                  │  (read-only SQL tools)     │
                                                  └────────────────────────────┘
```

## The five projects

| Project                          | Responsibility                                                              | I/O? |
| -------------------------------- | --------------------------------------------------------------------------- | ---- |
| `CloudEngAgent.Domain`           | Plain C# types: `Run`, `RunEvent`, `Persona`, `Workflow`, `BackendId`, etc. | No   |
| `CloudEngAgent.Application`      | Use cases (`StartRun`, `CancelRun`, …), abstractions (`IRunStore`, `IWorkflowEngine`, `IPersonaRepository`, `IMcpToolRegistry`, `IChatClientFactory`). | No   |
| `CloudEngAgent.Infrastructure`   | Adapters: EF Core run store, YAML persona repo + watcher, secret resolver, chat client factory, HTTP MCP client. DI composition lives in `ServiceCollectionExtensions.cs`. | Yes  |
| `CloudEngAgent.Api`              | ASP.NET Core minimal API, AG-UI SSE writer, auth, rate limiting, OpenTelemetry. | Yes |
| `CloudEngAgent.Mcp.Server`       | Standalone MCP server exposing read-only SQL Server introspection tools.     | Yes  |

The dependency direction is strictly inward: `Api → Infrastructure → Application → Domain`.
`Application` and `Domain` never reference ASP.NET Core or any I/O library.
This is what lets the API run end-to-end with stub adapters in Development.

## Key abstractions

These interfaces (in `CloudEngAgent.Application`) are the seams you'll
encounter most often:

| Interface              | Purpose                                                                 | Real impl                       | Stub impl              |
| ---------------------- | ----------------------------------------------------------------------- | ------------------------------- | ---------------------- |
| `IRunStore`            | Persist `Run` aggregates and append `RunEvent`s.                        | `EfCoreRunStore`                | `InMemoryRunStore`     |
| `IWorkflowEngine`      | Execute a workflow for a given run, producing a stream of `RunEvent`s.  | `ChatClientWorkflowEngine`      | `StubWorkflowEngine`   |
| `IPersonaRepository`   | Look up personas by id; raise `PersonaChanged` on hot reload.           | `YamlPersonaRepository`         | `InMemoryPersonaRepository` |
| `IChatClientFactory`   | Resolve an `IChatClient` for a given `BackendId`.                       | One per backend (Azure OpenAI, OpenAI, GitHub Models, Anthropic). | n/a |
| `IBackendSecretResolver` | Resolve `ApiKeyRef` → secret value (Key Vault → config → env var).    | `BackendSecretResolver`         | n/a                    |
| `IMcpToolRegistry`     | List & invoke MCP tools, namespaced as `mcp:<server>.<tool>`.           | `HttpMcpToolRegistry`           | `EmptyMcpToolRegistry` |

DI selection happens in
[`ServiceCollectionExtensions.cs`](../src/CloudEngAgent.Infrastructure/ServiceCollectionExtensions.cs)
and in `Program.cs` for environment-conditional fallbacks (e.g., in-memory
fallbacks are only allowed in `Development`).

## Request lifecycle: starting a run

1. **Client** authenticates (Entra in production, dev-bypass in `Development`)
   and `POST /v1/runs` with `{ workflowId, input }`.
2. **API endpoint** validates the request, calls the `StartRunHandler` in
   `Application`.
3. **Handler** creates a `Run` aggregate, persists it via `IRunStore`, and
   schedules execution via `IWorkflowEngine`.
4. **Workflow engine** runs the workflow's entry persona:
   - **Stub mode**: emits a canonical AG-UI sequence (handoff → text deltas
     → tool call/result). No external calls. Default in Development.
   - **Real mode**: resolves the persona's backend, calls
     `IChatClient.GetStreamingResponseAsync`, and emits a `TextDelta` per
     non-empty chunk. Persona `Guardrails` map onto `ChatOptions`.
5. Each `RunEvent` is appended to the run's event buffer (bounded — see
   `Runs:EventBufferSize`).
6. **Client** mints an SSE token (`POST /v1/runs/{id}/sse-token`) and connects
   to `GET /v1/runs/{id}/events?token=…`.
7. The **AG-UI SSE writer** (`src/CloudEngAgent.Api/Sse/`) translates each
   `RunEvent` into an AG-UI event frame (see the mapping table in the main
   README) and flushes it down the wire.
8. On completion, error, or cancellation the stream emits `RUN_FINISHED` /
   `RUN_ERROR` and closes.

## SSE security model (why two endpoints?)

`EventSource` in browsers cannot send custom auth headers, so the API uses a
two-step pattern:

1. Authenticated `POST /v1/runs/{id}/sse-token` mints a short-lived
   `Microsoft.AspNetCore.DataProtection`-signed token bound to
   `(runId, subjectId, exp)`.
2. The browser opens `GET /v1/runs/{id}/events?token=…`, and the server
   validates signature, expiry, and runId binding before opening the stream.

Lifetime defaults to 120 s, clamped between 30 s and 600 s
(`Sse:TokenLifetimeSeconds`).

## Personas: YAML + hot reload

Personas live as one YAML file per persona under `personas/` (configurable via
`Personas:Directory`). The `YamlPersonaRepository` registers a
`FileSystemWatcher`, debounces bursts (~500 ms), diffs the new snapshot
against the previous one, and raises `IPersonaRepository.PersonaChanged` for
every add/update/remove. Invalid YAML is logged and the previous snapshot is
kept — the API never crashes on a bad file.

Schema and an example are in the
[Personas (M4)](../README.md#personas-m4) section of the README.

## LLM backends: pluggable via `IChatClientFactory`

Each `AgentPersona` has a `BackendId` (e.g. `azure-openai`, `openai`,
`github-models`, `anthropic`). The factory looks up the configuration under
`Backends:<id>`, resolves the auth (managed identity for Azure, API key
otherwise), and returns an `IChatClient`. Adding a new backend is a localized
change — see [`development.md`](development.md#add-a-new-llm-backend).

## MCP: server *and* client

CloudEngAgent plays both sides of the MCP protocol:

- **`CloudEngAgent.Mcp.Server`** hosts read-only SQL introspection tools
  (`list_databases`, `list_tables`, `describe_table`, `sample_rows`). All
  identifiers pass an allow-list and are validated against
  `INFORMATION_SCHEMA` before being interpolated; all values go through
  `SqlParameter`. There is no DDL/DML surface.
- **`HttpMcpToolRegistry`** in `Infrastructure` is the *client*: it discovers
  tools from configured MCP servers (`Mcp:Client:Servers`), namespaces them as
  `mcp:<server>.<tool>`, and exposes them to the workflow engine. Servers
  that fail to list are skipped with a warning so a single broken server
  doesn't take down the whole catalog.

## Configuration & secrets

- **Configuration** is layered: `appsettings.json` → `appsettings.<Env>.json`
  → user-secrets (Development) → environment variables → command-line.
- **Secrets** (LLM keys, MCP bearer tokens) are resolved in this order:
  1. Azure Key Vault if `KeyVault:Uri` is set.
  2. `IConfiguration["Secrets:<ref>"]` (good for user-secrets).
  3. Environment variable (kebab-case → `UPPER_SNAKE_CASE`).
- **Production guardrails**: empty CORS origins, missing DB connection
  string, or a real workflow engine without a configured backend all cause
  fail-fast at startup. The same conditions degrade gracefully in
  `Development`.

## Observability & ops

- **Health**: `/healthz` (liveness) and `/readyz` (readiness; checks DB).
- **OpenTelemetry**: opt-in via `OpenTelemetry:Enabled` + `OtlpEndpoint`.
- **Rate limiting**: separate read/write permits per minute, configurable
  under `RateLimiting`.
- **Container**: `src/CloudEngAgent.Api/Dockerfile` builds a non-root image
  with a `HEALTHCHECK` against `/healthz`.

## Where to look in the code

| You want to…                             | Look at                                                      |
| ---------------------------------------- | ------------------------------------------------------------ |
| Add a new HTTP endpoint                  | `src/CloudEngAgent.Api/Endpoints/`                           |
| Change run-start behavior                | `src/CloudEngAgent.Application/Runs/`                        |
| Tweak DI / pick a different adapter      | `src/CloudEngAgent.Infrastructure/ServiceCollectionExtensions.cs`, `src/CloudEngAgent.Api/Program.cs` |
| Add an LLM backend                       | `src/CloudEngAgent.Infrastructure/Backends/`                 |
| Add or change an MCP tool                | `src/CloudEngAgent.Mcp.Server/Tools/`                        |
| Change AG-UI event mapping               | `src/CloudEngAgent.Api/Sse/`                                 |
| Add a persona                            | `personas/<id>.yaml` (no code change needed)                 |
| Persistence schema / migrations          | `src/CloudEngAgent.Infrastructure/Persistence/`              |

Now you have the map. For task-by-task recipes, see
[`development.md`](development.md).
