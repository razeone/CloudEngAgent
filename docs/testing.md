# Testing guide

## Test project map

| Project                                     | Kind         | Needs Docker? | Notes                                                |
| ------------------------------------------- | ------------ | ------------- | ---------------------------------------------------- |
| `tests/CloudEngAgent.Domain.Tests`          | Unit         | No            | Pure domain types; fast, deterministic.              |
| `tests/CloudEngAgent.Api.Tests`             | Unit / WAF   | No            | Uses `WebApplicationFactory` with stub adapters.     |
| `tests/CloudEngAgent.Mcp.Server.Tests`      | Unit         | No            | MCP tool argument validation, SQL guards.            |
| `tests/CloudEngAgent.Infrastructure.Tests`  | Integration  | **Yes**       | Spins up SQL Server via [Testcontainers].            |

[Testcontainers]: https://dotnet.testcontainers.org/

## Running tests

The CI pipeline runs the unit projects and the integration project in
separate jobs. To mirror it locally:

```bash
dotnet build --configuration Release -warnaserror

dotnet test tests/CloudEngAgent.Domain.Tests       --configuration Release --no-build
dotnet test tests/CloudEngAgent.Api.Tests          --configuration Release --no-build
dotnet test tests/CloudEngAgent.Mcp.Server.Tests   --configuration Release --no-build
dotnet test tests/CloudEngAgent.Infrastructure.Tests --configuration Release --no-build
```

`--no-build` reuses the assemblies you just built, which is faster and
guarantees you're testing exactly what compiled.

### Filter to a single test or class

```bash
dotnet test tests/CloudEngAgent.Api.Tests --filter "FullyQualifiedName~Runs"
dotnet test tests/CloudEngAgent.Domain.Tests --filter "FullyQualifiedName=CloudEngAgent.Domain.Tests.RunTests.Cancel_marks_run_as_cancelled"
```

The filter syntax is the standard
[`dotnet test --filter`](https://learn.microsoft.com/dotnet/core/testing/selective-unit-tests)
expression.

### Code coverage

CI collects coverage with `--collect:"XPlat Code Coverage"` and uploads the
results as the `unit-test-results` artifact. To do the same locally:

```bash
dotnet test tests/CloudEngAgent.Api.Tests \
  --configuration Release --no-build \
  --collect:"XPlat Code Coverage" \
  --results-directory ./TestResults
```

A `coverage.cobertura.xml` is written under `TestResults/<guid>/`. Use
`reportgenerator` (or your IDE's coverage view) to render it.

## The integration tests need Docker

`CloudEngAgent.Infrastructure.Tests` uses Testcontainers to spin up a real
SQL Server 2022 container per test collection. The fixture
([`MsSqlContainerFixture.cs`](../tests/CloudEngAgent.Infrastructure.Tests/MsSqlContainerFixture.cs))
detects whether Docker is available:

- ✅ Docker available → tests run against the container.
- 🟡 Docker missing → tests are **skipped with a clear reason**, not failed.

This means you can develop and run the unit suite without Docker, and let CI
exercise the integration path. If you're touching `Persistence/` code,
**always** run the integration tests locally before pushing.

First-time runs are slow because Docker has to pull the SQL Server image
(several GB). Subsequent runs reuse the cached image.

## Writing a new test

- Match the existing layout: one test class per production class, nested under
  a folder that mirrors the source folder.
- Name tests with the `Method_state_expectation` convention (e.g.
  `Cancel_when_already_finished_throws`).
- Prefer **arrange/act/assert** with a blank line between sections. No
  comments needed if the structure is clear.
- For API tests, use the existing `WebApplicationFactory`-based base class
  (look in `tests/CloudEngAgent.Api.Tests/` for the pattern). It pre-wires
  the stub adapters so tests are hermetic.
- For integration tests, depend on the `MsSql` xUnit collection so the
  shared container fixture is reused:
  ```csharp
  [Collection("MsSql")]
  public sealed class MyNewIntegrationTests(MsSqlContainerFixture fixture) { ... }
  ```
  Honor `fixture.SkipReason` if it's set so your test skips cleanly when
  Docker is unavailable.

## Common test gotchas

- **`xUnit1031` warnings**: don't `.Result` / `.Wait()` async tasks in tests
  — make the test method `async Task` and `await`.
- **`-warnaserror` doesn't apply to test projects**, but warnings still show
  up in the build log. Treat them as bugs anyway; they're often pointing at a
  real issue in the test code.
- **Hanging tests**: usually a missing `await` or a deadlocked `IAsyncLifetime`.
  Run with `--blame` to capture a hang dump:
  ```bash
  dotnet test tests/CloudEngAgent.Api.Tests --blame --blame-hang-timeout 60s
  ```
- **Flaky integration tests**: try a clean container by deleting any
  `cloudeng-mssql`/Testcontainers leftovers (`docker ps -a`, then
  `docker rm <id>`). The fixture creates fresh containers per run, but a
  stuck previous run can hold the port.

## CI parity checklist

Before opening a PR, run:

```bash
dotnet build --configuration Release -warnaserror
dotnet test tests/CloudEngAgent.Domain.Tests       --configuration Release --no-build
dotnet test tests/CloudEngAgent.Api.Tests          --configuration Release --no-build
dotnet test tests/CloudEngAgent.Mcp.Server.Tests   --configuration Release --no-build
dotnet test tests/CloudEngAgent.Infrastructure.Tests --configuration Release --no-build  # if Docker is available
```

If all four pass locally, CI will almost certainly pass too.
