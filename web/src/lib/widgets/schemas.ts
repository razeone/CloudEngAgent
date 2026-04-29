import { z } from 'zod';
import { WIDGET_TYPES } from './types';

export const placementSchema = z.object({
  surface: z.enum(['timeline', 'messages', 'artifacts']),
  order: z.number().int().optional(),
});

export const artifactSchema = z.object({
  id: z.string(),
  name: z.string().optional(),
  contentType: z.string().optional(),
  sizeBytes: z.number().int().nonnegative().optional(),
});

export const widgetStateSchema = z.object({
  id: z.string(),
  type: z.enum(WIDGET_TYPES),
  status: z.enum(['pending', 'streaming', 'complete', 'error', 'awaiting-input']),
  title: z.string().optional(),
  summary: z.string().optional(),
  placement: placementSchema,
  props: z.record(z.unknown()),
  artifact: artifactSchema.optional(),
  createdAt: z.string().optional(),
  updatedAt: z.string().optional(),
});

export const resultTableProps = z.object({
  columns: z.array(z.object({ key: z.string(), label: z.string().optional() })),
  rows: z.array(z.record(z.unknown())),
  totalRows: z.number().int().nonnegative().optional(),
});

export const barChartProps = z.object({
  series: z.array(z.object({ name: z.string(), value: z.number() })),
  xLabel: z.string().optional(),
  yLabel: z.string().optional(),
});

export const kpiCardsProps = z.object({
  cards: z.array(
    z.object({
      label: z.string(),
      value: z.union([z.string(), z.number()]),
      delta: z.string().optional(),
      tone: z.enum(['positive', 'negative', 'neutral']).optional(),
    }),
  ),
});

export const findingsListProps = z.object({
  findings: z.array(
    z.object({
      id: z.string(),
      severity: z.enum(['info', 'warn', 'high', 'critical']),
      title: z.string(),
      detail: z.string().optional(),
    }),
  ),
});

export const ddlDiffProps = z.object({
  before: z.string(),
  after: z.string(),
  language: z.string().optional(),
});

export const approvalCardProps = z.object({
  prompt: z.string(),
  options: z.array(z.string()).default(['approve', 'reject']),
  metadata: z.record(z.unknown()).optional(),
});

export const fileDownloadProps = z.object({
  filename: z.string(),
  description: z.string().optional(),
});

export const markdownReportProps = z.object({
  markdown: z.string(),
});

export const widgetPropSchemas = {
  'result-table': resultTableProps,
  'bar-chart': barChartProps,
  'kpi-cards': kpiCardsProps,
  'findings-list': findingsListProps,
  'ddl-diff': ddlDiffProps,
  'approval-card': approvalCardProps,
  'file-download': fileDownloadProps,
  'markdown-report': markdownReportProps,
} as const;
