import type { SharedState } from '@/lib/agui/stateReducer';

export function MessagesPane({ state }: { state: SharedState }) {
  return (
    <aside className="border-r border-border h-full overflow-y-auto p-3">
      <h2 className="text-sm font-semibold mb-2">Messages</h2>
      <ul className="space-y-2 text-xs">
        {state.messages.map((m) => (
          <li key={m.id} className="rounded bg-muted/40 p-2">
            <div className="font-mono text-[10px] uppercase text-muted-foreground">{m.role}</div>
            <div className="whitespace-pre-wrap">{m.text}</div>
          </li>
        ))}
        {state.messages.length === 0 && <li className="text-muted-foreground">(no messages)</li>}
      </ul>
    </aside>
  );
}
