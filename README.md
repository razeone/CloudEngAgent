# CloudEngAgent

A multi-agent .NET 10 backend that empowers DBAs and power users with AI-driven exploration, analysis, and administration of SQL databases. Connects to multiple LLM backends and exposes operational tools through MCP (Model Context Protocol). Streams results to clients over an [AG-UI](https://docs.ag-ui.com/) compatible API.

> **New here?** Start with [`CONTRIBUTING.md`](CONTRIBUTING.md) and the
> [`docs/`](docs/) folder — they cover prerequisites, a guided
> [first-day onboarding](docs/onboarding.md), an
> [architecture overview](docs/architecture.md), and recipes for common
> changes. The rest of this README is the operator/user reference.

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

If a `ConnectionStrings:Runs` connection string is configured, apply EF Core migrations first:

```powershell
dotnet ef database update --project src/CloudEngAgent.Infrastructure --startup-project src/CloudEngAgent.Api
```

## Database

This project uses EF Core with SQL Server for run persistence. The database is optional in Development but required in production.

### Prerequisites

Install SQL Server locally or via Docker:

```powershell
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=Your_strong_Passw0rd!" `
  -p 1433:1433 -d --name cloudeng-mssql `
  mcr.microsoft.com/mssql/server:2022-latest
```

### Set the connection string

Configure `ConnectionStrings:Runs` via user-secrets (recommended for Development):

```powershell
dotnet user-secrets init --project src/CloudEngAgent.Api
dotnet user-secrets set --project src/CloudEngAgent.Api `
  "ConnectionStrings:Runs" `
  "Server=localhost,1433;Database=cloudeng_runs;User Id=sa;Password=Your_strong_Passw0rd!;TrustServerCertificate=true"
```

Alternatively, set the `ConnectionStrings__Runs` environment variable.

### Apply migrations

Install the EF Core CLI tool if needed:

```powershell
dotnet tool install --global dotnet-ef
```

Apply pending migrations:

```powershell
dotnet ef database update --project src/CloudEngAgent.Infrastructure --startup-project src/CloudEngAgent.Api
```

### Fallback behavior

- **Development**: If `ConnectionStrings:Runs` is empty, the API falls back to an in-memory store (data is lost on restart) and logs a warning.
- **Production & other environments**: If `ConnectionStrings:Runs` is empty, the API fails fast at startup with an `InvalidOperationException`.

The `/readyz` health probe returns **503 Service Unavailable** when the database is unreachable.

## LLM backends

CloudEngAgent routes agent calls to multiple LLM providers via an `IChatClientFactory`. Each agent persona declares its preferred backend, which is resolved from the `Backends` configuration section.

### Supported backends

| Backend | Status | Details |
|---------|--------|---------|
| `azure-openai` | ✅ Implemented | Azure OpenAI Service |
| `openai` | ✅ Implemented | OpenAI API (gpt-4o, gpt-4-turbo, etc.) |
| `github-models` | ✅ Implemented | GitHub Models (Azure-hosted inference) |
| `anthropic` | ✅ Implemented | Anthropic API (Claude models via `Anthropic.SDK`) |
| `azure-foundry` | 🔜 M3.5 | Azure AI Foundry (deferred) |

### Per-backend configuration

Each backend is configured under `Backends:<id>` in `appsettings.json`. Example (all backends):

```jsonc
"Backends": {
  "azure-openai": {
    "Endpoint": "https://my-aoai.openai.azure.com/",
    "Deployment": "gpt-4o",
    "AuthMode": "ManagedIdentity"                    // or "ApiKey" (requires ApiKeyRef)
  },
  "openai": {
    "Model": "gpt-4o",
    "ApiKeyRef": "openai-key",                        // resolved from secrets (see below)
    "AuthMode": "ApiKey"
  },
  "github-models": {
    "Model": "gpt-4o",
    "ApiKeyRef": "github-pat",                        // GitHub Personal Access Token (PAT)
    "AuthMode": "ApiKey"
  },
  "anthropic": {
    "Model": "claude-opus-4-7",
    "ApiKeyRef": "anthropic-key",
    "AuthMode": "ApiKey"
  },
  "azure-foundry": {
    "Endpoint": "https://my-foundry-endpoint/",
    "Model": "some-model-id",
    "ApiKeyRef": "foundry-key",
    "AuthMode": "ApiKey"
  }
}
```

### Authentication modes

- **ManagedIdentity** (Azure backends only): Uses `DefaultAzureCredential` (Entra ID managed identity in production, `az login` credentials locally).
- **ApiKey**: Requires `ApiKeyRef` pointing to a secret that is resolved as described below.

### Secret resolution order

When a backend specifies `AuthMode: "ApiKey"` with an `ApiKeyRef` (e.g., `"openai-key"`), the secret is resolved in this order:

1. **Azure Key Vault** (if `KeyVault:Uri` is configured): The secret name is retrieved from the vault with process-lifetime caching.
2. **Configuration** (fallback): `IConfiguration["Secrets:<ApiKeyRef>"]` — check `appsettings.json` or user-secrets.
3. **Environment variable** (final fallback): Kebab-case is converted to UPPER_SNAKE_CASE (e.g., `openai-key` → `OPENAI_KEY`).

If none of these sources provide a value, startup fails with `InvalidOperationException`.

### Local development: user-secrets

For Development, store secrets in the user-secrets store (stored encrypted locally; not in the repo):

```powershell
dotnet user-secrets init --project src/CloudEngAgent.Api

# Set OpenAI API key
dotnet user-secrets set --project src/CloudEngAgent.Api "Secrets:openai-key" "sk-..."

# Set GitHub PAT for GitHub Models
dotnet user-secrets set --project src/CloudEngAgent.Api "Secrets:github-pat" "ghp_..."

# Set Anthropic key
dotnet user-secrets set --project src/CloudEngAgent.Api "Secrets:anthropic-key" "sk-ant-..."
```

Then run the API:

```powershell
dotnet run --project src/CloudEngAgent.Api
```

### Persona → backend mapping

Each `AgentPersona` has a `BackendId` field that selects the backend configuration:

```csharp
public class AgentPersona
{
    public string Name { get; set; }
    public BackendId Backend { get; set; }  // e.g., "azure-openai", "openai", "github-models"
    public string Role { get; set; }
    // ...
}
```

When the workflow engine invokes an agent, it uses the persona's `Backend` to look up the configuration and instantiate the appropriate `IChatClient`.

## Personas (M4)

Personas live as YAML files in a directory configured via `Personas:Directory` (defaults to `./personas` relative to the content root). Each file describes a single persona and is hot-reloaded when the file changes on disk.

### Schema

```yaml
id: explorer                 # required, matches the persona's address
name: Schema Explorer        # required, human-readable name
backend: azure-openai        # required, one of: azure-openai, azure-foundry, openai, github-models, anthropic
systemPrompt: |              # required
  You discover database structure: schemas, tables, views, columns, indexes,
  foreign keys. Use the SQL MCP tools to introspect; never modify data.
tools: []                    # optional, list of mcp:<server>.<tool> refs
guardrails:                  # optional
  maxTokens: 2048
  temperature: 0.1
  topP: 0.95
```

### Hot reload

The repository registers a `FileSystemWatcher` on the personas directory and debounces bursts of file events (~500 ms). On every reload it diffs the new snapshot against the previous one and raises `IPersonaRepository.PersonaChanged` for every add, update, or removal. Consumers can subscribe to the event to invalidate caches or re-prime workflows.

If the directory is not present at startup the API falls back to the bundled `InMemoryPersonaRepository` in `Development` and fails fast in any other environment.

### Adding a persona

1. Create `personas/<id>.yaml` with the schema above.
2. Save the file — the watcher reloads automatically; the new persona becomes available on the next `GET /v1/personas` call.
3. Invalid YAML or a missing required field is logged and the previous snapshot is kept (the API does not crash).

## Workflow engine (M5.1)

`IWorkflowEngine` has two implementations:

| Implementation              | Behavior                                                                            |
| --------------------------- | ----------------------------------------------------------------------------------- |
| `StubWorkflowEngine`        | Emits a canonical AG-UI sequence (handoff → text deltas → tool call/result). Useful for end-to-end tests without an LLM key. |
| `ChatClientWorkflowEngine`  | Resolves the workflow's entry persona, calls `IChatClient.GetStreamingResponseAsync`, and streams `TextDelta` events for each non-empty chunk. Persona `Guardrails` map to `ChatOptions` (`MaxOutputTokens`, `Temperature`, `TopP`). |

Selection is driven by `WorkflowEngine:Mode`:

```jsonc
{
  "WorkflowEngine": {
    "Mode": "Auto"   // Auto (default) | Real | Stub
  }
}
```

- **Auto** — picks `Real` when `Backends:azure-openai:Endpoint` (or `Backends:azure-foundry:Endpoint`) is set; otherwise picks `Stub` in `Development` and fails fast in any other environment. Key-based backends (openai, github-models, anthropic) are not auto-detected because the seed `appsettings.json` includes placeholder `ApiKeyRef` values; opt in explicitly with `WorkflowEngine:Mode=Real`.
- **Real** — always uses `ChatClientWorkflowEngine`. Required when you actually want LLM responses.
- **Stub** — always uses `StubWorkflowEngine`. Useful for CI and offline development.

Multi-agent orchestration via `Microsoft.Agents.AI.Workflows` (orchestrator → {explorer, analyst, …}) is the M5.2 follow-up; the M5.1 slice runs the workflow's entry persona as a single agent.

## MCP Server (M6)

`CloudEngAgent.Mcp.Server` is an ASP.NET Core 10 host that exposes a small set of **read-only** SQL Server introspection tools over the [Model Context Protocol](https://modelcontextprotocol.io). It is the server side of the MCP integration; the API project consumes it through the `IMcpToolRegistry` HTTP client (M7).

### Run locally

```powershell
dotnet run --project src\CloudEngAgent.Mcp.Server
```

The server listens on a Kestrel-assigned port and exposes:

| Method | Route      | Description                                                             |
| ------ | ---------- | ----------------------------------------------------------------------- |
| GET    | `/healthz` | Liveness probe.                                                         |
| ANY    | `/mcp`     | MCP endpoint (`Streamable HTTP` transport from `ModelContextProtocol.AspNetCore`). |

### Configuration

Connection strings live under `Mcp:SqlServer:ConnectionStrings`. Each entry maps a logical database name (the value MCP callers pass as the `database` argument) to a SQL Server ADO.NET connection string. The first non-empty entry is used when callers omit `database`.

```jsonc
{
  "Mcp": {
    "SqlServer": {
      "ConnectionStrings": {
        "default": "Server=localhost,1433;Database=master;User Id=sa;Password=...;TrustServerCertificate=true",
        "warehouse": "Server=warehouse.example.com;Database=dw;Authentication=Active Directory Default"
      }
    }
  }
}
```

For local development use user-secrets (the project ships with a user-secrets id):

```powershell
dotnet user-secrets set --project src\CloudEngAgent.Mcp.Server `
  "Mcp:SqlServer:ConnectionStrings:default" `
  "Server=localhost,1433;Database=master;User Id=sa;Password=Your_strong_Passw0rd!;TrustServerCertificate=true"
```

If no connection strings are configured the server still boots (and `/healthz` returns OK), but invoking any tool returns a structured `InvalidParams` error so callers get a clear "no connection configured" message.

### Tool catalog

| Tool                | Arguments                                                | Returns                                                                                            |
| ------------------- | -------------------------------------------------------- | -------------------------------------------------------------------------------------------------- |
| `list_databases`    | `database?`                                              | User database names from `sys.databases` (system DBs excluded).                                    |
| `list_tables`       | `database?`                                              | `{schema, name}` for every base table in `INFORMATION_SCHEMA.TABLES`.                              |
| `describe_table`    | `schema`, `table`, `database?`                           | Columns from `INFORMATION_SCHEMA.COLUMNS` (name, type, nullability, length/precision).             |
| `sample_rows`       | `schema`, `table`, `top` (1–100, default 10), `database?`| Up to N rows as `{column → value}` dictionaries. `SELECT TOP (n) * FROM ...`.                      |
| `top_queries`       | `database?`, `top` (1–50, default 10)                    | Top-N queries by total CPU from `sys.dm_exec_query_stats` + `sys.dm_exec_sql_text`. Capped at 50.  |
| `missing_indexes`   | `database?`                                              | Missing-index recommendations (top 50 by `avg_total_user_cost * avg_user_impact * (seeks+scans)`). |
| `wait_stats`        | `database?`, `top` (1–50, default 20)                    | Top wait types from `sys.dm_os_wait_stats` with idle/system noise filtered out. Capped at 50.      |
| `blocking_sessions` | `database?`                                              | Currently blocked sessions (`blocking_session_id <> 0`) with blocker login + blocked SQL text.     |
| `fk_graph`          | `database?`                                              | Foreign-key graph (one row per FK column; composite FKs produce multiple rows). No row cap.        |
| `column_stats`      | `schema`, `table`, `database?`                           | Per-column statistics from `sys.stats` + `sys.dm_db_stats_properties`.                             |
| `db_health_checks`  | `database?`                                              | Curated read-only checks (auto_close, auto_shrink, recovery model, page verify, backups, etc.).    |

### Safety model

- All tool **values** are sent as `SqlParameter` instances — never concatenated into SQL.
- All tool **identifiers** (schema/table) must pass a strict allow-list (`^[A-Za-z_][A-Za-z0-9_]{0,127}$`) **and** be present in `INFORMATION_SCHEMA` before being bracket-quoted and interpolated. Anything else is rejected up-front with `InvalidParams`.
- `sample_rows` enforces a hard cap of 100 rows regardless of the requested `top`.
- `SqlException` details are logged in full but only the SQL `Number` + the first line of the message are returned to the client; stack traces never leak through MCP.
- All tools are **read-only** — there is no DDL/DML surface.

## MCP Client (M7)

The API can act as an MCP **client**, discovering tools from one or more MCP servers and exposing them through `IMcpToolRegistry` for use by the workflow engine. Tool references are namespaced as `mcp:<server>.<tool>` so the same tool name on different servers never collides.

Configure under `Mcp:Client` in `appsettings.json` (or any other `IConfiguration` source — env vars, Key Vault, etc.):

```jsonc
{
  "Mcp": {
    "Client": {
      "Servers": [
        {
          "Name":     "primary",                       // ^[a-zA-Z0-9_-]+$
          "Endpoint": "http://localhost:5010/mcp",     // HTTP(S) URL of the MCP server
          "AuthType": "None"                            // None | Bearer
        },
        {
          "Name":     "ops",
          "Endpoint": "https://ops-mcp.example.com/mcp",
          "AuthType": "Bearer",
          "TokenRef": "ops-mcp-token"                   // resolved via IBackendSecretResolver
        }
      ]
    }
  }
}
```

Behaviour:

- When `Mcp:Client:Servers` is empty (default), the API registers a no-op `EmptyMcpToolRegistry` and no MCP traffic is generated.
- Otherwise, `HttpMcpToolRegistry` lazily opens one `IMcpClient` per configured server (SDK `SseClientTransport` over HTTP) on first use.
- `ListToolsAsync` aggregates tools from all servers and caches the merged listing for 60 s. Servers that are unreachable (or that throw during listing) are **skipped** with a warning so a single broken server can't take the whole catalog down.
- `InvokeAsync(ToolRef, …)` parses the server prefix off the qualified name (`<server>.<tool>`) and routes the call to the matching `IMcpClient`. MCP error responses are surfaced as `McpToolInvocationResult { IsError = true }` rather than thrown.
- On qualified-name collisions the **last server wins** and a warning is logged.
- When `AuthType: "Bearer"` is set, the registry resolves `TokenRef` through `IBackendSecretResolver` (same path used for LLM backend keys: in-memory config → environment variable → Key Vault if configured) and adds an `Authorization: Bearer <token>` header to outbound MCP traffic.
- Sessions are disposed cleanly on host shutdown via the `IAsyncDisposable` registered alongside the registry.

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
  "Cors":            { "AllowedOrigins": ["http://localhost:5173"] },
  "Runs":            { "MaxConcurrent": 32, "EventBufferSize": 1024 },
  "Sse":             { "TokenLifetimeSeconds": 120 },
  "RateLimiting":    { "Enabled": true, "WritePermitsPerMinute": 60, "ReadPermitsPerMinute": 600 },
  "OpenTelemetry":   { "Enabled": false, "ServiceName": "CloudEngAgent.Api", "OtlpEndpoint": "" },
  "Entra":           { "TenantId": "", "Audience": "api://cloud-eng-agent" },
  "KeyVault":        { "Uri": "" },                   // Optional: Azure Key Vault for secret resolution
  "Backends":        { /* per-backend connection settings */ },
  "Secrets":         { /* in-memory fallback secrets (development only) */ },
  "Mcp":             { "Servers": [ /* MCP servers */ ] },
  "Personas":        { "Directory": "./personas", "Watch": true },
  "WorkflowEngine":  { "Mode": "Auto" },                // Auto | Real | Stub
  "ConnectionStrings": { "Runs": "" }
}
```

Key notes:

- **Backends**: See the "LLM backends" section above for per-backend configuration and secret resolution.
- **KeyVault:Uri**: If set, API keys referenced in `Backends` are resolved from the vault. If empty, falls back to `Secrets` config or environment variables.
- **ConnectionStrings:Runs**: Set to a SQL Server connection string to enable EF Core persistence. If empty in Development, falls back to in-memory. Required in production.
- In non-Development environments the app fails fast at startup if `Cors:AllowedOrigins` is empty.

## Container

```powershell
docker build -t cloudeng-agent-api -f src/CloudEngAgent.Api/Dockerfile .
docker run --rm -p 8080:8080 cloudeng-agent-api
```

The container runs as non-root and exposes a `HEALTHCHECK` against `/healthz`.

## Roadmap

See the in-session plan for the full P0/P1/P2 backlog. Milestones:

- **M2 (EF Core)**: ✅ In progress — SQL Server persistence and migrations have landed; integration tests & handler atomicity are in this wave.
- **M3 (real LLM backends)**: ✅ Complete — Azure OpenAI, OpenAI, GitHub Models, and Anthropic adapters wired through `IChatClientFactory`. Azure Foundry deferred to M3.5.
- **M4 (YAML personas + hot reload)**: ✅ Complete — see the "Personas (M4)" section above.
- **M5.1 (single-agent real LLM execution)**: ✅ Complete — see the "Workflow engine (M5.1)" section above.
- **M5.2** (multi-agent graph orchestration via `Microsoft.Agents.AI.Workflows`)
- **M6 (MCP SQL server)**: ✅ Complete — see the "MCP Server (M6)" section above.
- **M7 (MCP client wiring)**: ✅ Complete — see the "MCP Client (M7)" section above.

Forward-looking features (P3) include resumption, multi-tenancy, persona overlays, a workflow DSL, human-in-the-loop tool approval, cost/token telemetry, and saved investigations.
