import type { AgUiFrame } from './events';
import type { AgUiHandle } from './client';

export interface FakeOpts {
  frames: AgUiFrame[];
  onFrame: (frame: AgUiFrame) => void;
  delayMs?: number;
}

interface FakeRegistration {
  onFrame: (frame: AgUiFrame) => void;
  runId: string | null;
}

export const fakeClientRegistry = new Set<FakeRegistration>();

export function connectFake(opts: FakeOpts): AgUiHandle {
  const delay = opts.delayMs ?? 200;
  let cancelled = false;
  const reg: FakeRegistration = { onFrame: opts.onFrame, runId: null };
  fakeClientRegistry.add(reg);

  (async () => {
    for (const frame of opts.frames) {
      if (cancelled) return;
      if (frame.type === 'RunStarted') reg.runId = frame.runId;
      if (delay > 0) await new Promise((r) => setTimeout(r, delay));
      if (cancelled) return;
      opts.onFrame(frame);
    }
  })();

  return {
    close: () => {
      cancelled = true;
      fakeClientRegistry.delete(reg);
    },
  };
}

export function dispatchFakeInputReceived(payload: Record<string, unknown>): void {
  for (const reg of fakeClientRegistry) {
    queueMicrotask(() => {
      reg.onFrame({
        type: 'Custom',
        runId: reg.runId ?? 'r1',
        name: 'input.received',
        payload,
      });
    });
  }
}
