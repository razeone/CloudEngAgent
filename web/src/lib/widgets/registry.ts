import type { ComponentType } from 'react';
import type { WidgetState, WidgetType } from './types';
import { ResultTable } from '@/components/widgets/ResultTable';
import { BarChart } from '@/components/widgets/BarChart';
import { KpiCards } from '@/components/widgets/KpiCards';
import { FindingsList } from '@/components/widgets/FindingsList';
import { DdlDiff } from '@/components/widgets/DdlDiff';
import { ApprovalCard } from '@/components/widgets/ApprovalCard';
import { FileDownload } from '@/components/widgets/FileDownload';
import { MarkdownReport } from '@/components/widgets/MarkdownReport';

export interface WidgetRenderProps {
  id: string;
  state: WidgetState;
  runId: string;
}

export type WidgetRenderer = ComponentType<WidgetRenderProps>;

export const widgetRegistry: Record<WidgetType, WidgetRenderer> = {
  'result-table': ResultTable,
  'bar-chart': BarChart,
  'kpi-cards': KpiCards,
  'findings-list': FindingsList,
  'ddl-diff': DdlDiff,
  'approval-card': ApprovalCard,
  'file-download': FileDownload,
  'markdown-report': MarkdownReport,
};

export function getRenderer(type: WidgetType): WidgetRenderer {
  return widgetRegistry[type];
}
