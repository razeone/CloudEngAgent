# Onboarding: your first day on CloudEngAgent

This is a guided walkthrough that takes you from "I just cloned the repo" to
"I have the API running, I've made a small change, and I understand what I'm
looking at." Plan for ~30 minutes.

## 0. Install prerequisites

See the [Prerequisites](../CONTRIBUTING.md#prerequisites) section in
`CONTRIBUTING.md`. The non-negotiables are:

- **.NET 10 SDK (preview channel)** — match the CI version.
- **Docker** — only required for the integration test project; the API itself
  can run without it.

Verify:

```bash
dotnet --version          # should be 10.0.x
docker info > /dev/null   # should succeed silently
```

## 1. Clone & restore

```bash
git clone https://github.com/razeone/CloudEngAgent.git
cd CloudEngAgent
dotnet restore
```

`dotnet restore` reads `Directory.Packages.props` (central package management)
and the per-project `.csproj` files. If you see version errors, your local SDK
is probably older than the CI one.

## 2. Build everything

```bash
dotnet build --configuration Release -warnaserror
```

`-warnaserror` matches CI. If this is green, your toolchain is good.

## 3. Run the unit tests

```bash
dotnet test tests/CloudEngAgent.Domain.Tests       --no-build --configuration Release
dotnet test tests/CloudEngAgent.Api.Tests          --no-build --configuration Release
dotnet test tests/CloudEngAgent.Mcp.Server.Tests   --no-build --configuration Release
```

These don't need Docker. If you also have Docker running, you can do:

```bash
dotnet test tests/CloudEngAgent.Infrastructure.Tests --no-build --configuration Release
```

The MS SQL Testcontainer fixture skips gracefully when Docker isn't available
(see [`MsSqlContainerFixture.cs`](../tests/CloudEngAgent.Infrastructure.Tests/MsSqlContainerFixture.cs)).

## 4. Start the API

```bash
dotnet run --project src/CloudEngAgent.Api
```

You should see logs like:

```
info: Microsoft.Hosting.Lifetime[14] Now listening on: http://localhost:5xxx
```

In a second terminal, smoke-test it:

```bash
curl -s http://localhost:5xxx/healthz                       # → "Healthy"
curl -s http://localhost:5xxx/v1/personas | head            # → JSON list of personas
curl -s http://localhost:5xxx/v1/workflows | head           # → JSON list of workflows
```

> **Why does this work without any configuration?** In Development the API
> auto-selects the **Stub** workflow engine when no LLM endpoint is configured
> and falls back to an in-memory run store when `ConnectionStrings:Runs` is
> empty. Both fallbacks log a warning so you can see them in the console.

### Run an end-to-end stub workflow

```bash
# 1. Start a run
curl -s -X POST http://localhost:5xxx/v1/runs \
     -H "Content-Type: application/json" \
     -d '{"workflowId":"investigate","input":{"prompt":"hello"}}'

# Response includes runId; use it below.

# 2. Mint an SSE token
TOKEN=$(curl -s -X POST http://localhost:5xxx/v1/runs/<runId>/sse-token | jq -r .token)

# 3. Stream the AG-UI events
curl -N "http://localhost:5xxx/v1/runs/<runId>/events?token=$TOKEN"
```

You'll see the canonical AG-UI event sequence (`RUN_STARTED`,
`TEXT_MESSAGE_*`, `TOOL_CALL_*`, `RUN_FINISHED`) emitted by the stub engine.

## 5. Tour the code

Open these files in order — they're a good "intro reading list":

1. [`src/CloudEngAgent.Api/Program.cs`](../src/CloudEngAgent.Api/Program.cs) —
   composition root, middleware pipeline, endpoint mapping.
2. [`src/CloudEngAgent.Infrastructure/ServiceCollectionExtensions.cs`](../src/CloudEngAgent.Infrastructure/ServiceCollectionExtensions.cs) —
   where adapters are wired into DI.
3. [`src/CloudEngAgent.Domain/`](../src/CloudEngAgent.Domain/) — pure types
   (`Run`, `RunEvent`, `Persona`, `Workflow`). No I/O, no framework refs.
4. [`src/CloudEngAgent.Application/Runs/`](../src/CloudEngAgent.Application/Runs/) —
   use-case handlers (start/cancel/stream).
5. [`src/CloudEngAgent.Api/Sse/`](../src/CloudEngAgent.Api/Sse/) — the AG-UI
   event writer that translates `RunEvent` into SSE frames.
6. [`personas/`](../personas/) — six YAML personas; pick `explorer.yaml` to
   see the schema in practice.

For the full architectural picture, read
[`architecture.md`](architecture.md) next.

## 6. Make your first change

A safe "hello world" change to confirm the loop:

1. Open [`src/CloudEngAgent.Api/Endpoints/`](../src/CloudEngAgent.Api/Endpoints/)
   and find the personas endpoint.
2. Add a new `GET /v1/ping` endpoint that returns `{ "pong": true }`.
3. Add a test in `tests/CloudEngAgent.Api.Tests` that asserts the route
   returns `200` and the expected JSON.
4. Run `dotnet test tests/CloudEngAgent.Api.Tests`.
5. Revert it (or keep it as a learning artifact in your local branch).

If steps 1–4 work, you have everything you need to start on a real ticket.

## 7. Set up secrets (only when you need real LLMs / databases)

You can skip this until you actually need to talk to an LLM or persist runs.
When you do, follow the [LLM backends](../README.md#llm-backends) and
[Database](../README.md#database) sections of the main README. The short
version:

```bash
dotnet user-secrets init --project src/CloudEngAgent.Api

# Pick whichever backend(s) you want
dotnet user-secrets set --project src/CloudEngAgent.Api "Secrets:openai-key"     "sk-..."
dotnet user-secrets set --project src/CloudEngAgent.Api "Secrets:github-pat"     "ghp_..."
dotnet user-secrets set --project src/CloudEngAgent.Api "Secrets:anthropic-key"  "sk-ant-..."

# Or run a model locally with Ollama (no key required) — set the endpoint/model
# in appsettings (or via --Backends:ollama:Endpoint / --Backends:ollama:Model).
# Example: install Ollama, then `ollama pull llama3.1:8b` and point
# Backends:ollama:Endpoint at http://localhost:11434.

# And/or a SQL Server connection string for the run store
dotnet user-secrets set --project src/CloudEngAgent.Api \
    "ConnectionStrings:Runs" \
    "Server=localhost,1433;Database=cloudeng_runs;User Id=sa;Password=...;TrustServerCertificate=true"
```

Switch to the real workflow engine with:

```bash
dotnet run --project src/CloudEngAgent.Api -- --WorkflowEngine:Mode Real
```

## 8. Common first-day gotchas

- **`error NU1605` / version downgrade on restore** — you're probably on a
  stable .NET SDK. Install the 10 preview channel.
- **`-warnaserror` build fails after editing a file** — the analyzer flagged
  something. Read the warning; nullability and unused-using rules are common.
- **API exits immediately on startup in `Production` mode** — required config
  is missing (CORS origins, DB connection string, or a backend endpoint).
  Stick to `Development` until you've configured those.
- **`/v1/runs/{id}/events` returns 401** — you forgot to mint and pass the
  short-lived SSE token (`POST /v1/runs/{id}/sse-token` first).
- **Integration tests hang or fail with a Docker error** — the Testcontainers
  MsSql image is several GB; the first pull can take a while. Re-run after the
  pull completes, or skip them locally and let CI cover that path.

## 9. You're set 🎉

From here:

- Pick an issue or roadmap item (see the [Roadmap](../README.md#roadmap) in
  the main README).
- Read [`development.md`](development.md) for the recipe matching your task.
- Open a draft PR early; small reviewable PRs are strongly preferred.
