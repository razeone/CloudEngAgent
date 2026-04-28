# Contributing to CloudEngAgent

Welcome! 👋 This guide will get you from a fresh clone to a green build, your
first passing test, and an opened pull request. If anything here is wrong or
unclear, please open an issue or PR — onboarding fixes are always welcome.

> **TL;DR**
>
> ```bash
> git clone https://github.com/razeone/CloudEngAgent.git
> cd CloudEngAgent
> dotnet restore
> dotnet build
> dotnet test
> dotnet run --project src/CloudEngAgent.Api
> ```
>
> Then open `http://localhost:<port>/openapi/v1.json` (port logged on startup).

---

## Table of contents

1. [Prerequisites](#prerequisites)
2. [Getting the code](#getting-the-code)
3. [First build & test](#first-build--test)
4. [Running the services locally](#running-the-services-locally)
5. [Project layout at a glance](#project-layout-at-a-glance)
6. [Development workflow](#development-workflow)
7. [Coding conventions](#coding-conventions)
8. [Testing](#testing)
9. [Submitting a pull request](#submitting-a-pull-request)
10. [Where to go next](#where-to-go-next)

---

## Prerequisites

| Tool                | Version                | Purpose                                    |
| ------------------- | ---------------------- | ------------------------------------------ |
| [.NET SDK]          | **10.0 (preview)**     | Build & run all projects                   |
| Git                 | any recent             | Source control                             |
| Docker              | recent                 | SQL Server container, integration tests    |
| An IDE              | VS 2022+, VS Code, Rider | Editing & debugging C# 13                |
| (optional) `dotnet-ef` | latest              | EF Core migrations (`dotnet tool install --global dotnet-ef`) |

[.NET SDK]: https://dotnet.microsoft.com/download/dotnet/10.0

The CI pipeline pins `dotnet-version: 10.0.x` with `dotnet-quality: preview`
(see [`.github/workflows/ci.yml`](.github/workflows/ci.yml)). Use the same
channel locally to avoid restore mismatches.

> **Why .NET 10 preview?** The solution targets `net10.0` and uses the latest
> language version and analysis level (see `Directory.Build.props`).
> `TreatWarningsAsErrors` is on for non-test projects.

## Getting the code

```bash
git clone https://github.com/razeone/CloudEngAgent.git
cd CloudEngAgent
```

The repo is a single .NET solution (`CloudEngAgent.slnx`) with central package
management (`Directory.Packages.props`) — never pin a version inside an
individual `.csproj`.

## First build & test

```bash
dotnet restore
dotnet build --configuration Release -warnaserror
dotnet test
```

Mirror the CI commands when you want a faithful local run:

```bash
dotnet test tests/CloudEngAgent.Domain.Tests       --configuration Release --no-build
dotnet test tests/CloudEngAgent.Api.Tests          --configuration Release --no-build
dotnet test tests/CloudEngAgent.Mcp.Server.Tests   --configuration Release --no-build
# Integration tests require Docker (Testcontainers spins up SQL Server):
dotnet test tests/CloudEngAgent.Infrastructure.Tests --configuration Release --no-build
```

If Docker isn't available, the integration tests skip with a clear reason
rather than failing — see [`MsSqlContainerFixture.cs`](tests/CloudEngAgent.Infrastructure.Tests/MsSqlContainerFixture.cs).

## Running the services locally

The two runnable hosts are the API and the MCP server.

```bash
# API (AG-UI streaming + workflow runs)
dotnet run --project src/CloudEngAgent.Api

# MCP server (read-only SQL introspection tools)
dotnet run --project src/CloudEngAgent.Mcp.Server
```

Both bind to a Kestrel-assigned port (printed on startup). Useful endpoints:

- `GET /healthz` — liveness
- `GET /readyz` — readiness (Api only; checks the database)
- `GET /openapi/v1.json` — OpenAPI document (Development only)
- `GET /v1/personas`, `GET /v1/workflows`, `POST /v1/runs` — see
  [`README.md`](README.md#api-surface-v1) for the full list.

By default the API runs in **Stub** workflow mode (no LLM key required) and
falls back to an in-memory run store if `ConnectionStrings:Runs` is empty.
That means a fresh clone runs end-to-end with **zero configuration**. To wire
real backends, see the LLM, persona, and MCP sections of the main `README.md`.

## Project layout at a glance

```
src/
  CloudEngAgent.Domain/         Pure domain types (no I/O)
  CloudEngAgent.Application/    Use cases, abstractions, handlers
  CloudEngAgent.Infrastructure/ Adapters (EF Core, MCP HTTP client, secrets, persona repo)
  CloudEngAgent.Api/            ASP.NET Core minimal API + AG-UI SSE writer
  CloudEngAgent.Mcp.Server/     Read-only SQL introspection MCP server
tests/
  CloudEngAgent.Domain.Tests/
  CloudEngAgent.Api.Tests/
  CloudEngAgent.Mcp.Server.Tests/
  CloudEngAgent.Infrastructure.Tests/   Integration (needs Docker)
personas/                       YAML persona definitions (hot-reloaded)
docs/                           Onboarding & contributor docs (you are here ↗)
```

The architecture follows a classic ports-and-adapters pattern: `Domain` and
`Application` have no I/O; `Infrastructure` and `Api` plug in adapters via DI
in [`ServiceCollectionExtensions.cs`](src/CloudEngAgent.Infrastructure/ServiceCollectionExtensions.cs).

For a deeper tour see [`docs/architecture.md`](docs/architecture.md).

## Development workflow

1. **Create a branch** off `main`:
   `git checkout -b feature/<short-topic>` or `fix/<short-topic>`.
2. **Make focused changes.** Keep PRs small and reviewable.
3. **Build with warnings-as-errors** before pushing:
   `dotnet build --configuration Release -warnaserror`.
4. **Run the relevant tests** (unit + any integration touched).
5. **Open a draft PR early** if you want feedback; mark ready when green.

Common day-to-day tasks (adding a persona, a backend, an MCP tool, an API
endpoint) are documented step-by-step in
[`docs/development.md`](docs/development.md).

## Coding conventions

These are enforced by [`.editorconfig`](.editorconfig) and the analyzers
configured in `Directory.Build.props`. Highlights:

- **C# style**
  - File-scoped namespaces (`namespace Foo;`).
  - `var` is preferred for built-in types and when the type is apparent.
  - Braces required (`csharp_prefer_braces = true:warning`).
  - Async methods **must** end in `Async` (naming rule).
- **Nullability**: `<Nullable>enable</Nullable>`. `CS8600/02/03/25` are warnings.
  Don't suppress them — fix the root cause.
- **Warnings as errors**: All non-test projects fail on warnings. Tests are
  exempt so test-only warnings don't block development.
- **Centrally managed packages**: Add new package versions in
  `Directory.Packages.props`, then reference by name only in the `.csproj`.
- **Indentation**: 4 spaces in C#, 2 spaces in JSON / YAML.
- **No secrets in source**: use `dotnet user-secrets`, env vars, or Key Vault
  (see the README's "Secret resolution order" section).

## Testing

| Project                                  | Kind         | Notes                                       |
| ---------------------------------------- | ------------ | ------------------------------------------- |
| `CloudEngAgent.Domain.Tests`             | Unit         | Pure domain logic; runs anywhere.           |
| `CloudEngAgent.Api.Tests`                | Unit / WAF   | `WebApplicationFactory` in-memory.          |
| `CloudEngAgent.Mcp.Server.Tests`         | Unit         | MCP tool input validation & SQL guards.     |
| `CloudEngAgent.Infrastructure.Tests`     | Integration  | Spins up SQL Server via Testcontainers.     |

Run a single test class or method:

```bash
dotnet test tests/CloudEngAgent.Api.Tests --filter "FullyQualifiedName~Runs"
```

More details and tips: [`docs/testing.md`](docs/testing.md).

## Submitting a pull request

Before requesting review, please confirm:

- [ ] Code builds with `-warnaserror` locally.
- [ ] All affected tests pass; new behavior is covered by tests.
- [ ] No secrets, connection strings, or PATs are committed.
- [ ] Public APIs, configuration keys, or persona schemas have docs/README updates.
- [ ] The PR description explains *what* changed and *why*, and links any issue.

CI will re-run `restore → build → unit tests → integration tests` on every
push. A green CI is required before merge.

## Where to go next

- 🚀 [`docs/onboarding.md`](docs/onboarding.md) — a guided first-day walkthrough.
- 🏛️ [`docs/architecture.md`](docs/architecture.md) — how the layers fit together.
- 🛠️ [`docs/development.md`](docs/development.md) — recipes for common changes.
- 🧪 [`docs/testing.md`](docs/testing.md) — test strategy and tips.
- 📖 [`README.md`](README.md) — the full operator/user reference.

Happy hacking! 💙
