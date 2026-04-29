import { applyPatch, deepClone, type Operation } from 'fast-json-patch';
import type { AgUiFrame, JsonPatchOp } from './events';
import type { WidgetState } from '@/lib/widgets/types';

export type RunStatus = 'idle' | 'running' | 'finished' | 'error';

export interface MessageChunk {
  id: string;
  role: string;
  text: string;
  stepId?: string;
}

export interface StepEntry {
  stepId: string;
  agent?: string;
  status: 'running' | 'finished' | 'error';
}

export interface SharedState {
  runId: string | null;
  status: RunStatus;
  widgets: Record<string, WidgetState>;
  messages: MessageChunk[];
  steps: StepEntry[];
  customEvents: Array<{ name: string; payload?: Record<string, unknown> }>;
  raw: Record<string, unknown>;
}

export function createInitialState(): SharedState {
  return {
    runId: null,
    status: 'idle',
    widgets: {},
    messages: [],
    steps: [],
    customEvents: [],
    raw: { widgets: {} },
  };
}

export function applyPatches(
  doc: Record<string, unknown>,
  patches: JsonPatchOp[],
): Record<string, unknown> {
  let next = deepClone(doc) as Record<string, unknown>;
  for (const p of patches) {
    try {
      const result = applyPatch(next, [p as Operation], true, true);
      next = result.newDocument as Record<string, unknown>;
    } catch (err) {
      if ((p.op === 'remove' || p.op === 'replace') && /path|OPERATION_PATH/i.test(String(err))) {
        continue;
      }
      throw err;
    }
  }
  return next;
}

function reconcileWidgets(state: SharedState, raw: Record<string, unknown>): SharedState {
  const widgetsRaw = (raw.widgets ?? {}) as Record<string, unknown>;
  const widgets: Record<string, WidgetState> = {};
  for (const [k, v] of Object.entries(widgetsRaw)) {
    if (v && typeof v === 'object') widgets[k] = v as WidgetState;
  }
  return { ...state, widgets, raw };
}

export function reduce(state: SharedState, frame: AgUiFrame): SharedState {
  switch (frame.type) {
    case 'RunStarted':
      return { ...createInitialState(), runId: frame.runId, status: 'running' };
    case 'RunFinished':
      return { ...state, status: frame.status === 'error' ? 'error' : 'finished' };
    case 'StepStarted':
      return {
        ...state,
        steps: [...state.steps, { stepId: frame.stepId, agent: frame.agent, status: 'running' }],
      };
    case 'StepFinished':
      return {
        ...state,
        steps: state.steps.map((s) =>
          s.stepId === frame.stepId
            ? { ...s, status: frame.status === 'error' ? 'error' : 'finished' }
            : s,
        ),
      };
    case 'TextMessageChunk': {
      const existing = state.messages.find((m) => m.id === frame.messageId);
      if (existing) {
        return {
          ...state,
          messages: state.messages.map((m) =>
            m.id === frame.messageId ? { ...m, text: m.text + frame.delta } : m,
          ),
        };
      }
      return {
        ...state,
        messages: [
          ...state.messages,
          {
            id: frame.messageId,
            role: frame.role ?? 'assistant',
            text: frame.delta,
            stepId: frame.stepId,
          },
        ],
      };
    }
    case 'StateSnapshot':
      return reconcileWidgets(state, deepClone(frame.state) as Record<string, unknown>);
    case 'StateDelta': {
      const next = applyPatches(state.raw, frame.patches);
      return reconcileWidgets(state, next);
    }
    case 'Custom':
      return {
        ...state,
        customEvents: [...state.customEvents, { name: frame.name, payload: frame.payload }],
      };
    default:
      return state;
  }
}
