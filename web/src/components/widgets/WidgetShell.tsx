import type { ReactNode } from 'react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import type { WidgetState, WidgetStatus } from '@/lib/widgets/types';

const STATUS_TO_VARIANT: Record<WidgetStatus, BadgeProps['variant']> = {
  pending: 'secondary',
  streaming: 'default',
  complete: 'success',
  error: 'destructive',
  'awaiting-input': 'warning',
};

export interface WidgetShellProps {
  state: WidgetState;
  children?: ReactNode;
}

export function WidgetShell({ state, children }: WidgetShellProps) {
  return (
    <Card data-widget-type={state.type} data-widget-id={state.id} className="my-2">
      <CardHeader className="flex-row items-center justify-between space-y-0">
        <CardTitle className="text-sm">{state.title ?? state.type}</CardTitle>
        <Badge variant={STATUS_TO_VARIANT[state.status]}>{state.status}</Badge>
      </CardHeader>
      <CardContent className="text-xs text-muted-foreground">
        {state.summary ? <p className="mb-2">{state.summary}</p> : null}
        {children}
      </CardContent>
    </Card>
  );
}
