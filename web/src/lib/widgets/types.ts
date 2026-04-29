export const WIDGET_TYPES = [
  'result-table',
  'bar-chart',
  'kpi-cards',
  'findings-list',
  'ddl-diff',
  'approval-card',
  'file-download',
  'markdown-report',
] as const;

export type WidgetType = (typeof WIDGET_TYPES)[number];

export type WidgetStatus = 'pending' | 'streaming' | 'complete' | 'error' | 'awaiting-input';

export type WidgetSurface = 'timeline' | 'messages' | 'artifacts';

export interface WidgetPlacement {
  surface: WidgetSurface;
  order?: number;
}

export interface ArtifactRef {
  id: string;
  name?: string;
  contentType?: string;
  sizeBytes?: number;
}

export interface WidgetState {
  id: string;
  type: WidgetType;
  status: WidgetStatus;
  title?: string;
  summary?: string;
  placement: WidgetPlacement;
  props: Record<string, unknown>;
  artifact?: ArtifactRef;
  createdAt?: string;
  updatedAt?: string;
}

export interface WidgetEntry {
  key: string;
  state: WidgetState;
}
