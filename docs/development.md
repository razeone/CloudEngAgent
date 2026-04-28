# Development recipes

Step-by-step instructions for the most common changes contributors make. Each
recipe assumes you've already worked through
[`onboarding.md`](onboarding.md).

## Before you start any change

```bash
git checkout main
git pull
git checkout -b feature/<short-topic>
dotnet build --configuration Release -warnaserror
dotnet test
```

Confirm a clean baseline before editing anything.

---

## Add a new persona

Personas are pure data — no code change required.

1. Create `personas/<id>.yaml` with the schema from the
   [Personas section](../README.md#personas-m4) of the README. Required
   fields: `id`, `name`, `backend`, `systemPrompt`.
2. Save the file. The `YamlPersonaRepository` watcher debounces ~500 ms and
   reloads automatically.
3. Verify: `curl http://localhost:5xxx/v1/personas` should include your new
   id; `GET /v1/personas/<id>` should return the full record.
4. If the file is invalid, the **previous** snapshot is kept and the failure
   is logged — check the API console for the parse error.

> Tip: copy `personas/explorer.yaml` as a starting point.

## Add a new LLM backend

Use this when you want to support a new provider (e.g., a hypothetical
`bedrock`).

1. **Domain**: add the new id to the `BackendId` value object in
   `src/CloudEngAgent.Domain/Backends/`.
2. **Configuration shape**: add a strongly-typed options record under
   `src/CloudEngAgent.Infrastructure/Backends/` (mirror the existing
   `OpenAiBackendOptions`, `AnthropicBackendOptions`, etc.).
3. **Adapter**: implement the new chat client adapter in the same folder.
   Resolve the API key (if any) through `IBackendSecretResolver` — never
   read the env var or config directly.
4. **Factory wiring**: register the adapter in the `IChatClientFactory`
   implementation so a `BackendId` of your new value resolves to your client.
5. **Composition**: bind the options section in
   `ServiceCollectionExtensions.cs` (`Backends:<id>`).
6. **Docs**: add a row to the "Supported backends" table in `README.md` and
   include a sample `Backends:<id>` block.
7. **Tests**: add unit tests for option binding and secret resolution; add an
   integration smoke test if the SDK supports an offline mode.

The `WorkflowEngine:Mode = Auto` heuristic only auto-detects Azure backends
today (because key-based backends ship with placeholder `ApiKeyRef` values).
Document the explicit `WorkflowEngine:Mode=Real` opt-in for your backend.

## Add a new MCP tool (server side)

MCP tools live in `src/CloudEngAgent.Mcp.Server/Tools/`. Tools must remain
**read-only** — the safety model documented in the
[MCP Server section](../README.md#mcp-server-m6) is enforced by review.

1. Define the tool method using the MCP server attributes already used by
   sibling tools (look at `list_tables.cs` / `describe_table.cs`).
2. **Validate identifiers**: any `schema` / `table` / `database` argument must
   pass the allow-list regex `^[A-Za-z_][A-Za-z0-9_]{0,127}$` **and** be
   present in `INFORMATION_SCHEMA` before being bracket-quoted into SQL.
3. **Use `SqlParameter`** for every value. Never string-concatenate values
   into SQL.
4. **Cap result sizes** as appropriate (see the 100-row hard cap on
   `sample_rows`).
5. Add tests in `tests/CloudEngAgent.Mcp.Server.Tests/` covering:
   valid happy path, invalid identifier (rejection), and missing connection
   string (clear `InvalidParams` error).
6. Document the new tool in the "Tool catalog" table in `README.md`.

## Add a new HTTP endpoint

1. Add a minimal-API mapping in `src/CloudEngAgent.Api/Endpoints/` next to
   the existing `MapXxxEndpoints` extension methods.
2. Wire it in `Program.cs` alongside the others (look for
   `app.MapPersonaEndpoints()`-style calls).
3. Keep the endpoint thin: validate inputs, call into a handler in
   `CloudEngAgent.Application`, return the result. Don't put domain logic in
   the endpoint.
4. If the endpoint mutates state, apply the **write** rate-limit policy; for
   reads use the **read** policy (`RateLimiting:WritePermitsPerMinute` /
   `ReadPermitsPerMinute`).
5. Add tests in `tests/CloudEngAgent.Api.Tests/` using
   `WebApplicationFactory`. Cover the auth path (unauthenticated → 401 in
   non-Development environments) and the happy path.
6. Update the "API surface (v1)" table in `README.md`.

## Change AG-UI event behaviour

The mapping from internal `RunEvent` to AG-UI SSE events lives in
`src/CloudEngAgent.Api/Sse/`. The mapping table in the README must stay in
sync with the writer — update both in the same PR.

When changing the writer:

- Preserve event ordering guarantees (e.g., `STEP_FINISHED` before
  `STEP_STARTED` on a handoff).
- Keep frames flushable individually — never buffer multiple events into a
  single SSE write without a deliberate reason.
- Add or update a writer unit test that asserts the byte-level output of the
  affected branch.

## Add or change a configuration key

1. Define the key under the matching `*Options` record (or add a new options
   record if introducing a new section).
2. Bind the section in `ServiceCollectionExtensions.cs` or `Program.cs`.
3. Add validation if the value is required in non-Development environments.
   The convention in this codebase is **fail fast at startup** with a clear
   `InvalidOperationException` rather than crashing later.
4. Update the "Configuration" highlights block and any per-feature section in
   `README.md`.
5. If the key is a secret, route it through `IBackendSecretResolver` instead
   of reading it directly.

## Add or modify an EF Core migration

1. Make your model change in `src/CloudEngAgent.Infrastructure/Persistence/`.
2. Add a migration (the design-time factory in the project supports this):
   ```bash
   dotnet ef migrations add <DescriptiveName> \
     --project src/CloudEngAgent.Infrastructure \
     --startup-project src/CloudEngAgent.Api
   ```
3. Inspect the generated migration. If it does anything destructive (drops a
   column, changes a key), call it out in the PR description and add a
   data-preserving step.
4. Apply locally and run the `Infrastructure.Tests` integration suite (Docker
   required) to confirm round-trip behaviour.
5. Migrations are applied at runtime; you do **not** need to commit a
   `database update` artifact. Just commit the generated migration files.

## Debugging tips

- **Console logging is your friend.** The default development log level
  surfaces backend selection, persona reloads, MCP server discovery, and SSE
  token issuance. Skim the startup log first when something behaves oddly.
- **Reproduce CI locally** with the same commands the workflow runs (see
  `CONTRIBUTING.md` → [First build & test](../CONTRIBUTING.md#first-build--test)).
- **Stub vs Real workflow engine**: most "but it works in dev" surprises are
  caused by the engine silently falling back to `Stub`. Set
  `WorkflowEngine:Mode=Real` to force the real path and surface missing
  configuration.
- **`/readyz` is your DB canary.** A 503 there means the EF Core context
  can't reach SQL Server — fix the connection string before debugging higher
  layers.
- **MCP discovery failures** appear as warnings, not errors, by design.
  Check the API console for `HttpMcpToolRegistry` log lines if a tool you
  expect isn't showing up in `ListToolsAsync`.

## When in doubt

- Match the surrounding code. Conventions (file layout, naming, DI patterns)
  are intentionally consistent across the projects.
- Prefer **small, reviewable PRs** over large drops. Open a draft early and
  iterate.
- Ask in the PR. A two-line "should this live in Application or
  Infrastructure?" comment saves a re-review later.
