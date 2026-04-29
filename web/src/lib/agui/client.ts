import { ALL_FRAME_TYPES, type AgUiFrame, type AgUiFrameType } from './events';

export interface ConnectOpts {
  apiBase: string;
  runId: string;
  token?: string;
  lastEventId?: string;
  onFrame: (frame: AgUiFrame) => void;
  onError?: (err: Event) => void;
}

export interface AgUiHandle {
  close: () => void;
}

export function connect(opts: ConnectOpts): AgUiHandle {
  const url = new URL(`${opts.apiBase || ''}/v1/runs/${opts.runId}/events`, window.location.origin);
  if (opts.token) url.searchParams.set('token', opts.token);
  if (opts.lastEventId) url.searchParams.set('lastEventId', opts.lastEventId);

  const es = new EventSource(url.toString(), { withCredentials: false });

  for (const t of ALL_FRAME_TYPES) {
    const type: AgUiFrameType = t;
    es.addEventListener(type, (ev: MessageEvent) => {
      try {
        const data = JSON.parse(ev.data);
        opts.onFrame({ type, ...data } as AgUiFrame);
      } catch {
        /* ignore malformed */
      }
    });
  }
  if (opts.onError) es.addEventListener('error', opts.onError);

  return { close: () => es.close() };
}
