import { WidgetShell } from './WidgetShell';
import { ddlDiffProps } from '@/lib/widgets/schemas';
import type { WidgetRenderProps } from '@/lib/widgets/registry';

export function DdlDiff({ state }: WidgetRenderProps) {
  const parsed = ddlDiffProps.safeParse(state.props);
  const lang = parsed.success ? parsed.data.language ?? 'sql' : 'sql';
  return (
    <WidgetShell state={state}>
      <span>diff ({lang})</span>
    </WidgetShell>
  );
}
