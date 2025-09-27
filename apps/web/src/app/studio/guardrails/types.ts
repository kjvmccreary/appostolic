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

export type GuardrailSystemPolicy = {
  id: string;
  slug: string;
  name: string;
  description?: string | null;
  version: number;
  createdAt: string;
  updatedAt?: string | null;
  definition: GuardrailDefinition;
};

export type GuardrailDenominationPolicy = {
  id: string;
  name: string;
  notes?: string | null;
  version: number;
  createdAt: string;
  updatedAt?: string | null;
  definition: GuardrailDefinition;
};

export type GuardrailActivityAction = 'draft_saved' | 'published' | 'preset_applied' | 'updated';

export type GuardrailActivityEntry = {
  policyId: string;
  tenantId: string;
  tenantName?: string | null;
  key: string;
  layer: GuardrailPolicyLayer | 'override';
  version: number;
  updatedByEmail?: string | null;
  action: GuardrailActivityAction;
  occurredAt: string;
  derivedFromPresetId?: string | null;
  isActive: boolean;
  publishedAt?: string | null;
};

export type GuardrailSuperadminSummary = {
  systemPolicies: GuardrailSystemPolicy[];
  presets: GuardrailDenominationPolicy[];
  activity: GuardrailActivityEntry[];
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
