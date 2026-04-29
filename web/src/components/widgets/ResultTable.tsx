import { WidgetShell } from './WidgetShell';
import { resultTableProps } from '@/lib/widgets/schemas';
import type { WidgetRenderProps } from '@/lib/widgets/registry';

export function ResultTable({ state }: WidgetRenderProps) {
  const parsed = resultTableProps.safeParse(state.props);
  const rowCount = parsed.success ? parsed.data.rows.length : 0;
  const total = parsed.success ? parsed.data.totalRows ?? rowCount : rowCount;
  return (
    <WidgetShell state={state}>
      <span>
        {rowCount} row{rowCount === 1 ? '' : 's'} (of {total})
      </span>
    </WidgetShell>
  );
}
