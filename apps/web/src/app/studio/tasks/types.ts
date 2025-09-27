export type TaskStatus = 'Pending' | 'Running' | 'Succeeded' | 'Failed' | 'Canceled';

export type GuardrailDecision = 'Allow' | 'Escalate' | 'Deny';

export type GuardrailMatch = {
  rule?: string;
  ruleType?: string;
  source?: string;
  sourceId?: string;
  layer?: string | null;
};

export type GuardrailResult = {
  decision?: string;
  reasonCode?: string;
  matchedSignals?: string[];
  matches?: GuardrailMatch[];
};

export type GuardrailMetadata = {
  evaluatedAt?: string;
  context?: {
    tenantId?: string;
    policyKey?: string;
    channel?: string;
    promptSummary?: string;
    signals?: string[];
    presetIds?: string[];
    requestedUserId?: string;
    evaluatedUserId?: string;
  };
  result?: GuardrailResult;
  trace?: unknown;
};

export type TaskSummary = {
  id: string;
  agentId: string;
  status: TaskStatus | string;
  createdAt: string;
  startedAt?: string | null;
  finishedAt?: string | null;
  totalTokens?: number | null;
  estimatedCostUsd?: number | null;
  guardrailDecision?: GuardrailDecision | null;
};

export type Task = TaskSummary & {
  totalPromptTokens?: number | null;
  totalCompletionTokens?: number | null;
  result?: unknown;
  error?: string | null;
  guardrailMetadata?: GuardrailMetadata | null;
};

export type Trace = {
  stepNumber: number;
  kind: string;
  name: string;
  durationMs: number;
  promptTokens: number | null;
  completionTokens: number | null;
  error?: string | null;
  input?: unknown;
  output?: unknown;
};
