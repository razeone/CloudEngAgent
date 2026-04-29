import { WidgetShell } from './WidgetShell';
import { markdownReportProps } from '@/lib/widgets/schemas';
import type { WidgetRenderProps } from '@/lib/widgets/registry';

export function MarkdownReport({ state }: WidgetRenderProps) {
  const parsed = markdownReportProps.safeParse(state.props);
  const len = parsed.success ? parsed.data.markdown.length : 0;
  return (
    <WidgetShell state={state}>
      <span>{len} chars of markdown</span>
    </WidgetShell>
  );
}
