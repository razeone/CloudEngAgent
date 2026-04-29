import { getRenderer } from '@/lib/widgets/registry';
import type { WidgetEntry } from '@/lib/widgets/types';

export function ArtifactsPane({ widgets, runId }: { widgets: WidgetEntry[]; runId: string }) {
  return (
    <aside className="border-l border-border h-full overflow-y-auto p-3">
      <h2 className="text-sm font-semibold mb-2">Artifacts</h2>
      {widgets.length === 0 && <p className="text-xs text-muted-foreground">(no artifacts)</p>}
      {widgets.map(({ key, state }) => {
        const Renderer = getRenderer(state.type);
        return <Renderer key={key} id={key} state={state} runId={runId} />;
      })}
    </aside>
  );
}
