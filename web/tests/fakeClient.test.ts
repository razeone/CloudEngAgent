import { describe, it, expect } from 'vitest';
import { connectFake } from '@/lib/agui/fakeClient';
import { createInitialState, reduce, type SharedState } from '@/lib/agui/stateReducer';
import type { AgUiFrame } from '@/lib/agui/events';
import fixture from '@/fixtures/perf-tuning-run.json';

describe('fakeClient + fixture', () => {
  it('replays fixture to 7 widgets all complete', async () => {
    let state: SharedState = createInitialState();
    await new Promise<void>((resolve) => {
      let count = 0;
      const total = (fixture as unknown as AgUiFrame[]).length;
      connectFake({
        frames: fixture as unknown as AgUiFrame[],
        delayMs: 0,
        onFrame: (f) => {
          state = reduce(state, f);
          count++;
          if (count === total) setTimeout(resolve, 0);
        },
      });
    });
    const widgets = Object.values(state.widgets);
    expect(widgets).toHaveLength(7);
    for (const w of widgets) expect(w.status).toBe('complete');
    const types = new Set(widgets.map((w) => w.type));
    expect(types).toEqual(
      new Set([
        'kpi-cards',
        'bar-chart',
        'result-table',
        'ddl-diff',
        'approval-card',
        'markdown-report',
        'file-download',
      ]),
    );
    expect(state.status).toBe('finished');
  });
});
