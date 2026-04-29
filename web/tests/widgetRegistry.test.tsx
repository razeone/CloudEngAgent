import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import { WIDGET_TYPES, type WidgetState, type WidgetType } from '@/lib/widgets/types';
import { getRenderer } from '@/lib/widgets/registry';

function makeState(type: WidgetType): WidgetState {
  const propsByType: Record<WidgetType, Record<string, unknown>> = {
    'result-table': { columns: [{ key: 'a' }], rows: [{ a: 1 }], totalRows: 1 },
    'bar-chart': { series: [{ name: 'x', value: 1 }] },
    'kpi-cards': { cards: [{ label: 'k', value: 1 }] },
    'findings-list': { findings: [{ id: 'f1', severity: 'info', title: 't' }] },
    'ddl-diff': { before: 'a', after: 'b', language: 'sql' },
    'approval-card': { prompt: 'ok?', options: ['approve', 'reject'] },
    'file-download': { filename: 'x.txt' },
    'markdown-report': { markdown: '# hi' },
  };
  return {
    id: `id-${type}`,
    type,
    status: 'complete',
    placement: { surface: 'timeline' },
    props: propsByType[type],
    artifact: type === 'file-download' ? { id: 'art_x', name: 'x.txt' } : undefined,
  };
}

describe('widgetRegistry', () => {
  for (const type of WIDGET_TYPES) {
    it(`renders ${type}`, () => {
      const Renderer = getRenderer(type);
      const state = makeState(type);
      const { container } = render(<Renderer id={state.id} state={state} runId="r1" />);
      expect(container.querySelector(`[data-widget-type="${type}"]`)).not.toBeNull();
    });
  }
});
