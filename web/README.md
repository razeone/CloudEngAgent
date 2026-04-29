# CloudEng Agent — Web Reference Console (`/web`)

A React + Vite + TypeScript reference frontend for the CloudEng Agent generative-UI MVP.
Renders an AG-UI event stream into a 3-pane Run Console:

```
┌──────────────┬───────────────────────────┬──────────────────┐
│  Messages    │  Timeline (widgets)       │  Artifacts       │
│  (per agent) │  result-table, bar-chart, │  file-download,  │
│              │  kpi-cards, findings,     │  markdown-report │
│              │  ddl-diff, approval-card  │                  │
└──────────────┴───────────────────────────┴──────────────────┘
```

This package is intentionally **independent of the .NET backend**: it ships a
`fakeClient` that replays a JSON fixture so the UI can be developed offline.

---

## Stack

| Concern        | Choice                                              |
| -------------- | --------------------------------------------------- |
| Build / dev    | Vite 5                                              |
| Language       | TypeScript 5 (strict, no `any`)                     |
| UI             | React 18 + Tailwind 3 + shadcn-style primitives     |
| State          | Hand-rolled reducer + `fast-json-patch` (RFC 6902)  |
| AG-UI client   | Hand-rolled `EventSource` (see deviation below)     |
| Tests          | Vitest + Testing Library + jsdom                    |
| Lint / format  | ESLint (`@typescript-eslint`) + Prettier            |

### A note on `@ag-ui/client`

The MVP plan calls out `@ag-ui/client` as the canonical client. At scaffold
time, that package is published at **0.0.52** (very early / unstable surface).
We deliberately ship a small hand-rolled `EventSource`-based client in
`src/lib/agui/client.ts` that:

- registers a listener per AG-UI event name (`RunStarted`, `RunFinished`,
  `StepStarted`, `StepFinished`, `TextMessageChunk`, `StateSnapshot`,
  `StateDelta`, `Custom`);
- supports `lastEventId` for reconnect/resume;
- accepts an opaque `token` query param.

When the upstream package stabilises (≥ 0.1.x with a documented event-name
contract), we'll swap this implementation behind the same `connect()` API.

---

## Scripts

| Command            | What it does                                                        |
| ------------------ | ------------------------------------------------------------------- |
| `npm run dev`      | Dev server with the **fake** client replaying the fixture           |
| `npm run dev:live` | Dev server pointed at the real AG-UI backend (`VITE_AGUI_FAKE=0`)   |
| `npm run build`    | Type-check then `vite build`                                        |
| `npm run preview`  | Preview the production build                                        |
| `npm run test`     | Run Vitest once                                                     |
| `npm run lint`     | ESLint with `--max-warnings=0`                                      |
| `npm run format`   | Prettier write                                                      |

## Env vars

| Var                  | Default | Purpose                                         |
| -------------------- | ------- | ----------------------------------------------- |
| `VITE_AGUI_FAKE`     | `1`     | `1`/unset → fake client; `0` → real backend     |
| `VITE_AGUI_API_BASE` | `''`    | e.g. `http://localhost:8080`                    |
| `VITE_AGUI_RUN_ID`   | `r1`    | Run id for the live SSE endpoint                |
| `VITE_AGUI_TOKEN`    | `''`    | Opaque token forwarded as `?token=…`            |

---

## Architecture

```
src/
  lib/
    agui/
      events.ts        AgUiFrame discriminated union (matches backend mapper)
      stateReducer.ts  reduce(state, frame) + applyPatches(doc, patches)
      client.ts        connect()    — real EventSource client
      fakeClient.ts    connectFake() + dispatchFakeInputReceived()
    widgets/
      types.ts         WIDGET_TYPES (8) + WidgetState
      schemas.ts       Zod schemas mirroring server JSON Schemas
      registry.ts      widgetRegistry: Record<WidgetType, Renderer>
  components/
    ui/                Button, Card, Badge (shadcn-style)
    widgets/           Stub renderer per widget + WidgetShell
    layout/            MessagesPane, TimelinePane, ArtifactsPane, RunConsole
  fixtures/
    perf-tuning-run.json  ~22 frames; end state = 7 complete widgets
```

### Widget canonical ids and JSON Pointer escaping

Widget keys use the canonical id format
`run:<runId>/step:<stepId>/agent:<agentId>/widget:<name>`. Because `/` is the
JSON Pointer path separator, every `/` in the key must be escaped as `~1`
(and any `~` as `~0`) in `StateDelta` paths. The fixture and tests both
exercise this — see `tests/stateReducer.test.ts`.

### Adding a 9th widget

1. Append the new type literal to `WIDGET_TYPES` in `src/lib/widgets/types.ts`.
2. Add a Zod schema for its `props` in `src/lib/widgets/schemas.ts`.
3. Create `src/components/widgets/<Name>.tsx` (use `WidgetShell` for chrome).
4. Register it in `src/lib/widgets/registry.ts`.
5. Extend the smoke test in `tests/widgetRegistry.test.tsx` (the loop over
   `WIDGET_TYPES` will pick it up; just supply a valid props sample).
6. Add a server-side schema + a fixture frame creating one.

---

## Fixture

`src/fixtures/perf-tuning-run.json` is the offline demo script. It models the
"performance investigation on the StackOverflow `Posts` table" demo from the
MVP plan: orchestrator → performance agent (KPIs / chart / slow-query table) →
analyst agent (DDL diff + approval gate + markdown report + file download).

To re-record it from a real backend, point a browser at the live console with
`VITE_AGUI_FAKE=0`, capture the SSE stream (e.g. `curl --no-buffer`), and
massage the lines into the `[ { type, … }, … ]` JSON shape.

## Testing

- `tests/stateReducer.test.ts` — snapshot/delta/escaping/no-op semantics.
- `tests/widgetRegistry.test.tsx` — every `WIDGET_TYPES` entry renders a
  `[data-widget-type="…"]` element.
- `tests/fakeClient.test.ts` — replays the fixture and asserts the end state
  is exactly **7 widgets, all `status: complete`**.
