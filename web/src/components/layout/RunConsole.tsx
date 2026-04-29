import { useMemo } from 'react';
import type { SharedState } from '@/lib/agui/stateReducer';
import type { WidgetEntry, WidgetState } from '@/lib/widgets/types';
import { MessagesPane } from './MessagesPane';
import { TimelinePane } from './TimelinePane';
import { ArtifactsPane } from './ArtifactsPane';

interface Routed {
  messages: WidgetEntry[];
  timeline: WidgetEntry[];
  artifacts: WidgetEntry[];
}

export function routeWidgets(widgets: Record<string, WidgetState>): Routed {
  const out: Routed = { messages: [], timeline: [], artifacts: [] };
  const entries: WidgetEntry[] = Object.entries(widgets).map(([key, state]) => ({ key, state }));
  entries.sort((a, b) => (a.state.placement.order ?? 0) - (b.state.placement.order ?? 0));
  for (const entry of entries) {
    const { type, placement } = entry.state;
    if (type === 'file-download' || type === 'markdown-report') {
      out.artifacts.push(entry);
      continue;
    }
    out[placement.surface].push(entry);
  }
  return out;
}

export function RunConsole({ state, runId }: { state: SharedState; runId: string }) {
  const routed = useMemo(() => routeWidgets(state.widgets), [state.widgets]);
  return (
    <div className="h-screen w-screen grid grid-cols-[320px_1fr_360px] bg-background text-foreground">
      <MessagesPane state={state} />
      <TimelinePane widgets={routed.timeline} runId={runId} />
      <ArtifactsPane widgets={routed.artifacts} runId={runId} />
    </div>
  );
}
