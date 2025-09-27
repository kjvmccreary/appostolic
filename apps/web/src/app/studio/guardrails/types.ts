import type {
  GuardrailDecision,
  GuardrailMatch,
  GuardrailTraceEntry,
  GuardrailPolicySnapshot,
} from '../tasks/types';

export type GuardrailPolicyLayer = 'tenantbase' | 'override' | 'draft';

export type GuardrailDefinition = {
  allow?: string[];
  deny?: string[];
  escalate?: string[];
  presets?: {
    denominations?: string[];
  };
  inherits?: string;
  [key: string]: unknown;
};

export type TenantGuardrailPolicy = {
  id: string;
  key: string;
  layer: GuardrailPolicyLayer;
  version: number;
  isActive: boolean;
  derivedFromPresetId?: string | null;
  createdByUserId?: string | null;
  updatedByUserId?: string | null;
  createdAt: string;
  updatedAt?: string | null;
  publishedAt?: string | null;
  metadata?: Record<string, unknown> | null;
  definition: GuardrailDefinition;
};

export type GuardrailPreset = {
  id: string;
  name: string;
  notes?: string | null;
  version: number;
};

export type GuardrailSnapshot = {
  decision: GuardrailDecision;
  reasonCode: string;
  matchedSignals: string[];
  policy: GuardrailPolicySnapshot;
  matches: GuardrailMatch[];
  trace: GuardrailTraceEntry[];
};

export type TenantGuardrailSummary = {
  key: string;
  policies: TenantGuardrailPolicy[];
  snapshot: GuardrailSnapshot;
  presets: GuardrailPreset[];
};
