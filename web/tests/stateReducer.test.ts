import { describe, it, expect } from 'vitest';
import { applyPatches, createInitialState, reduce } from '@/lib/agui/stateReducer';
import type { AgUiFrame } from '@/lib/agui/events';

describe('stateReducer', () => {
  it('StateSnapshot replaces widgets', () => {
    let s = createInitialState();
    s = reduce(s, {
      type: 'StateSnapshot',
      runId: 'r1',
      state: { widgets: { a: { id: 'a', type: 'kpi-cards', status: 'complete', placement: { surface: 'timeline' }, props: {} } } },
    });
    expect(Object.keys(s.widgets)).toEqual(['a']);
  });

  it('StateDelta add/replace/remove apply', () => {
    let s = createInitialState();
    s = reduce(s, {
      type: 'StateSnapshot',
      runId: 'r1',
      state: { widgets: { a: { id: 'a', type: 'kpi-cards', status: 'pending', placement: { surface: 'timeline' }, props: { v: 1 } } } },
    });
    s = reduce(s, {
      type: 'StateDelta',
      runId: 'r1',
      patches: [{ op: 'replace', path: '/widgets/a/status', value: 'complete' }],
    });
    expect(s.widgets.a.status).toBe('complete');
    s = reduce(s, {
      type: 'StateDelta',
      runId: 'r1',
      patches: [{ op: 'add', path: '/widgets/b', value: { id: 'b', type: 'bar-chart', status: 'complete', placement: { surface: 'timeline' }, props: {} } }],
    });
    expect(s.widgets.b).toBeDefined();
    s = reduce(s, {
      type: 'StateDelta',
      runId: 'r1',
      patches: [{ op: 'remove', path: '/widgets/a' }],
    });
    expect(s.widgets.a).toBeUndefined();
  });

  it('unknown op throws', () => {
    expect(() =>
      applyPatches({}, [{ op: 'frobnicate', path: '/x', value: 1 } as unknown as never]),
    ).toThrow();
  });

  it('remove on missing path is a no-op', () => {
    const next = applyPatches({ a: 1 }, [{ op: 'remove', path: '/missing' }]);
    expect(next).toEqual({ a: 1 });
  });

  it('replace on missing path is a no-op', () => {
    const next = applyPatches({ a: 1 }, [{ op: 'replace', path: '/missing', value: 2 }]);
    expect(next).toEqual({ a: 1 });
  });

  it('handles JSON pointer ~1 escaping for canonical widget ids', () => {
    const widgetKey = 'run:r1/step:s/agent:a/widget:w';
    const escaped = widgetKey.replace(/~/g, '~0').replace(/\//g, '~1');
    let s = createInitialState();
    s = reduce(s, {
      type: 'StateSnapshot',
      runId: 'r1',
      state: { widgets: { [widgetKey]: { id: widgetKey, type: 'kpi-cards', status: 'pending', placement: { surface: 'timeline' }, props: {} } } },
    } as AgUiFrame);
    s = reduce(s, {
      type: 'StateDelta',
      runId: 'r1',
      patches: [{ op: 'replace', path: `/widgets/${escaped}/status`, value: 'complete' }],
    });
    expect(s.widgets[widgetKey].status).toBe('complete');
  });
});
