import { WidgetShell } from './WidgetShell';
import { barChartProps } from '@/lib/widgets/schemas';
import type { WidgetRenderProps } from '@/lib/widgets/registry';

export function BarChart({ state }: WidgetRenderProps) {
  const parsed = barChartProps.safeParse(state.props);
  const count = parsed.success ? parsed.data.series.length : 0;
  return (
    <WidgetShell state={state}>
      <span>{count} series</span>
    </WidgetShell>
  );
}
