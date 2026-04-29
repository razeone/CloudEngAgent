import { WidgetShell } from './WidgetShell';
import { kpiCardsProps } from '@/lib/widgets/schemas';
import type { WidgetRenderProps } from '@/lib/widgets/registry';

export function KpiCards({ state }: WidgetRenderProps) {
  const parsed = kpiCardsProps.safeParse(state.props);
  const count = parsed.success ? parsed.data.cards.length : 0;
  return (
    <WidgetShell state={state}>
      <span>{count} KPI{count === 1 ? '' : 's'}</span>
    </WidgetShell>
  );
}
