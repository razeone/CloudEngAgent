import { useEffect, useState } from 'react';
import { connect } from '@/lib/agui/client';
import { connectFake } from '@/lib/agui/fakeClient';
import { createInitialState, reduce, type SharedState } from '@/lib/agui/stateReducer';
import { RunConsole } from '@/components/layout/RunConsole';
import fixture from '@/fixtures/perf-tuning-run.json';
import type { AgUiFrame } from '@/lib/agui/events';

const FAKE = (import.meta.env.VITE_AGUI_FAKE ?? '1') !== '0';
const RUN_ID = import.meta.env.VITE_AGUI_RUN_ID ?? 'r1';
const API_BASE = import.meta.env.VITE_AGUI_API_BASE ?? '';
const TOKEN = import.meta.env.VITE_AGUI_TOKEN ?? '';

export default function App() {
  const [state, setState] = useState<SharedState>(() => createInitialState());

  useEffect(() => {
    const onFrame = (frame: AgUiFrame) => {
      setState((prev) => reduce(prev, frame));
    };
    const handle = FAKE
      ? connectFake({ frames: fixture as unknown as AgUiFrame[], onFrame, delayMs: 200 })
      : connect({ apiBase: API_BASE, runId: RUN_ID, token: TOKEN, onFrame });
    return () => handle.close();
  }, []);

  return <RunConsole state={state} runId={RUN_ID} />;
}
