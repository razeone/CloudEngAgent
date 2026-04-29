import { useState } from 'react';
import { WidgetShell } from './WidgetShell';
import { approvalCardProps } from '@/lib/widgets/schemas';
import type { WidgetRenderProps } from '@/lib/widgets/registry';
import { Button } from '@/components/ui/button';
import { dispatchFakeInputReceived } from '@/lib/agui/fakeClient';

const FAKE = (import.meta.env.VITE_AGUI_FAKE ?? '1') !== '0';
const API_BASE = import.meta.env.VITE_AGUI_API_BASE ?? '';

type Phase = 'idle' | 'submitting' | 'submitted' | 'error';

export function ApprovalCard({ state, runId }: WidgetRenderProps) {
  const parsed = approvalCardProps.safeParse(state.props);
  const prompt = parsed.success ? parsed.data.prompt : '(invalid props)';
  const options = parsed.success ? parsed.data.options : ['approve', 'reject'];
  const [phase, setPhase] = useState<Phase>('idle');
  const [chosen, setChosen] = useState<string | null>(null);

  async function submit(choice: string) {
    setChosen(choice);
    setPhase('submitting');
    const payload = { widgetId: state.id, choice };
    try {
      if (FAKE) {
        dispatchFakeInputReceived(payload);
      } else {
        const res = await fetch(`${API_BASE}/v1/runs/${runId}/inputs`, {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify(payload),
        });
        if (!res.ok) throw new Error(`status ${res.status}`);
      }
      setPhase('submitted');
    } catch {
      setPhase('error');
    }
  }

  return (
    <WidgetShell state={state}>
      <p className="mb-2">{prompt}</p>
      <div className="flex gap-2">
        {options.map((opt) => (
          <Button
            key={opt}
            size="sm"
            variant={opt === 'reject' ? 'destructive' : 'default'}
            disabled={phase === 'submitting' || phase === 'submitted'}
            onClick={() => submit(opt)}
          >
            {opt}
          </Button>
        ))}
      </div>
      {phase !== 'idle' && (
        <p className="mt-2 text-xs">
          {phase === 'submitting' && `submitting ${chosen}…`}
          {phase === 'submitted' && `submitted: ${chosen}`}
          {phase === 'error' && `error submitting ${chosen}`}
        </p>
      )}
    </WidgetShell>
  );
}
