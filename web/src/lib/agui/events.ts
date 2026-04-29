export type JsonPatchOp = {
  op: 'add' | 'remove' | 'replace' | 'move' | 'copy' | 'test';
  path: string;
  value?: unknown;
  from?: string;
};

export type AgUiFrame =
  | { type: 'RunStarted'; runId: string; timestamp?: string }
  | { type: 'RunFinished'; runId: string; status?: string; timestamp?: string }
  | { type: 'StepStarted'; runId: string; stepId: string; agent?: string; timestamp?: string }
  | { type: 'StepFinished'; runId: string; stepId: string; status?: string; timestamp?: string }
  | {
      type: 'TextMessageChunk';
      runId: string;
      stepId?: string;
      messageId: string;
      delta: string;
      role?: string;
      timestamp?: string;
    }
  | { type: 'StateSnapshot'; runId: string; state: Record<string, unknown>; timestamp?: string }
  | { type: 'StateDelta'; runId: string; patches: JsonPatchOp[]; timestamp?: string }
  | {
      type: 'Custom';
      runId: string;
      name: string;
      payload?: Record<string, unknown>;
      timestamp?: string;
    };

export type AgUiFrameType = AgUiFrame['type'];

export const ALL_FRAME_TYPES: AgUiFrameType[] = [
  'RunStarted',
  'RunFinished',
  'StepStarted',
  'StepFinished',
  'TextMessageChunk',
  'StateSnapshot',
  'StateDelta',
  'Custom',
];
