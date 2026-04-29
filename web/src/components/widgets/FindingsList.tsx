import { WidgetShell } from './WidgetShell';
import { findingsListProps } from '@/lib/widgets/schemas';
import type { WidgetRenderProps } from '@/lib/widgets/registry';

export function FindingsList({ state }: WidgetRenderProps) {
  const parsed = findingsListProps.safeParse(state.props);
  const count = parsed.success ? parsed.data.findings.length : 0;
  return (
    <WidgetShell state={state}>
      <span>{count} finding{count === 1 ? '' : 's'}</span>
    </WidgetShell>
  );
}
