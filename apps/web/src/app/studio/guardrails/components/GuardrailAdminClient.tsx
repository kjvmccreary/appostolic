'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  CircularProgress,
  Divider,
  Grid,
  MenuItem,
  Select,
  Stack,
  TextField,
  Typography,
  Snackbar,
} from '@mui/material';
import type {
  GuardrailDefinition,
  GuardrailPreset,
  GuardrailSnapshot,
  TenantGuardrailPolicy,
  TenantGuardrailSummary,
} from '../types';
import type { GuardrailPolicySnapshot } from '../../tasks/types';

const emptyDefinition: GuardrailDefinition = { allow: [], deny: [], escalate: [] };

function normalizeList(values?: unknown): string[] {
  if (!Array.isArray(values)) return [];
  return Array.from(
    new Set(
      values
        .filter((item): item is string => typeof item === 'string' && item.trim().length > 0)
        .map((item) => item.trim()),
    ),
  ).sort((a, b) => a.localeCompare(b));
}

function coerceDefinition(definition?: GuardrailDefinition | null): GuardrailDefinition {
  if (!definition) return emptyDefinition;
  return {
    ...definition,
    allow: normalizeList(definition.allow),
    deny: normalizeList(definition.deny),
    escalate: normalizeList(definition.escalate),
    presets: definition.presets,
  };
}

function makeDefinitionString(definition: GuardrailDefinition): string {
  return JSON.stringify(definition, null, 2);
}

function comparePolicyLists(base: GuardrailDefinition, draft: GuardrailDefinition) {
  const baseAllow = normalizeList(base.allow);
  const baseDeny = normalizeList(base.deny);
  const baseEscalate = normalizeList(base.escalate);
  const draftAllow = normalizeList(draft.allow);
  const draftDeny = normalizeList(draft.deny);
  const draftEscalate = normalizeList(draft.escalate);

  const diff = (current: string[], incoming: string[]) => {
    const currentSet = new Set(current);
    const incomingSet = new Set(incoming);
    const shared = current.filter((item) => incomingSet.has(item));
    const added = incoming.filter((item) => !currentSet.has(item));
    const removed = current.filter((item) => !incomingSet.has(item));
    return { shared, added, removed };
  };

  return {
    allow: diff(baseAllow, draftAllow),
    deny: diff(baseDeny, draftDeny),
    escalate: diff(baseEscalate, draftEscalate),
  };
}

function policyRow(title: string, diff: ReturnType<typeof comparePolicyLists>['allow']) {
  if (diff.shared.length === 0 && diff.added.length === 0 && diff.removed.length === 0) {
    return (
      <Stack spacing={1} key={title}>
        <Typography variant="subtitle2" color="text.secondary">
          {title}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          None
        </Typography>
      </Stack>
    );
  }

  return (
    <Stack spacing={1} key={title}>
      <Typography variant="subtitle2" color="text.secondary">
        {title}
      </Typography>
      <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
        {diff.shared.map((value) => (
          <Chip key={`shared-${title}-${value}`} label={value} size="small" />
        ))}
        {diff.added.map((value) => (
          <Chip
            key={`added-${title}-${value}`}
            label={`+ ${value}`}
            size="small"
            color="success"
            variant="outlined"
          />
        ))}
        {diff.removed.map((value) => (
          <Chip
            key={`removed-${title}-${value}`}
            label={`– ${value}`}
            size="small"
            color="warning"
            variant="outlined"
          />
        ))}
      </Stack>
    </Stack>
  );
}

function SnapshotSummary({ snapshot }: { snapshot: GuardrailSnapshot }) {
  const severity =
    snapshot.decision === 'Deny'
      ? 'error'
      : snapshot.decision === 'Escalate'
        ? 'warning'
        : 'success';
  return (
    <Card variant="outlined">
      <CardContent>
        <Typography variant="h6">Current Guardrail Decision</Typography>
        <Alert severity={severity} sx={{ mt: 2 }}>
          <Stack spacing={1}>
            <Typography variant="subtitle1" fontWeight={600}>
              Decision: {snapshot.decision}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              Reason: {snapshot.reasonCode}
            </Typography>
            {snapshot.matchedSignals.length > 0 && (
              <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                {snapshot.matchedSignals.map((signal) => (
                  <Chip key={signal} label={signal} size="small" color="info" />
                ))}
              </Stack>
            )}
          </Stack>
        </Alert>
        <Divider sx={{ my: 2 }} />
        <Typography variant="subtitle2" gutterBottom>
          Effective Policy Snapshot
        </Typography>
        <PolicySnapshotList snapshot={snapshot.policy} />
      </CardContent>
    </Card>
  );
}

function PolicySnapshotList({ snapshot }: { snapshot: GuardrailPolicySnapshot }) {
  return (
    <Stack spacing={1}>
      <SnapshotRow title="Allow" values={snapshot.allow} chipColor="success" />
      <SnapshotRow title="Deny" values={snapshot.deny} chipColor="error" />
      <SnapshotRow title="Escalate" values={snapshot.escalate} chipColor="warning" />
    </Stack>
  );
}

function SnapshotRow({
  title,
  values,
  chipColor,
}: {
  title: string;
  values: readonly string[];
  chipColor: 'default' | 'success' | 'error' | 'warning';
}) {
  return (
    <Stack spacing={1}>
      <Typography variant="subtitle2" color="text.secondary">
        {title}
      </Typography>
      {values.length === 0 ? (
        <Typography variant="body2" color="text.secondary">
          None
        </Typography>
      ) : (
        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
          {values.map((value) => (
            <Chip key={`${title}-${value}`} label={value} size="small" color={chipColor} />
          ))}
        </Stack>
      )}
    </Stack>
  );
}

type BusyState = 'save' | 'publish' | 'reset' | null;

type Props = {
  summary: TenantGuardrailSummary;
};

export function GuardrailAdminClient({ summary }: Props) {
  const router = useRouter();
  const basePolicy = summary.policies.find((p) => p.layer === 'tenantbase');
  const draftPolicy = summary.policies.find((p) => p.layer === 'draft');

  const baseDefinition = useMemo(
    () => coerceDefinition(basePolicy?.definition ?? emptyDefinition),
    [basePolicy?.definition],
  );
  const initialDraftDefinition = useMemo(
    () => coerceDefinition(draftPolicy?.definition ?? baseDefinition),
    [draftPolicy?.definition, baseDefinition],
  );

  const [draftText, setDraftText] = useState(() => makeDefinitionString(initialDraftDefinition));
  const [selectedPreset, setSelectedPreset] = useState(
    draftPolicy?.derivedFromPresetId ?? basePolicy?.derivedFromPresetId ?? '',
  );
  const [busy, setBusy] = useState<BusyState>(null);
  const [error, setError] = useState<string | null>(null);
  const [snackbar, setSnackbar] = useState<string | null>(null);

  useEffect(() => {
    const nextDraft = coerceDefinition(draftPolicy?.definition ?? baseDefinition);
    setDraftText(makeDefinitionString(nextDraft));
    setSelectedPreset(draftPolicy?.derivedFromPresetId ?? basePolicy?.derivedFromPresetId ?? '');
  }, [
    draftPolicy?.definition,
    draftPolicy?.derivedFromPresetId,
    baseDefinition,
    basePolicy?.derivedFromPresetId,
  ]);

  const parsedDraft = useMemo(() => {
    try {
      return coerceDefinition(JSON.parse(draftText));
    } catch {
      return null;
    }
  }, [draftText]);

  const diff = useMemo(
    () => comparePolicyLists(baseDefinition, parsedDraft ?? initialDraftDefinition),
    [baseDefinition, parsedDraft, initialDraftDefinition],
  );

  const handleError = useCallback((message: string) => {
    setError(message);
  }, []);

  const jsonHeaders = useMemo(() => ({ 'content-type': 'application/json' }), []);

  const policyKey = summary.key || 'default';

  const handleSaveDraft = useCallback(async () => {
    let payloadDefinition: GuardrailDefinition;
    try {
      payloadDefinition = JSON.parse(draftText);
    } catch (err) {
      handleError(err instanceof Error ? err.message : 'Draft definition must be valid JSON.');
      return;
    }

    setBusy('save');
    try {
      const res = await fetch(
        `/api-proxy/guardrails/tenant/${encodeURIComponent(policyKey)}/draft`,
        {
          method: 'PUT',
          headers: jsonHeaders,
          cache: 'no-store',
          body: JSON.stringify({
            definition: payloadDefinition,
            derivedFromPresetId: selectedPreset || null,
          }),
        },
      );
      if (!res.ok) {
        throw new Error(`Draft save failed (${res.status})`);
      }
      setSnackbar('Draft saved');
      router.refresh();
    } catch (err) {
      handleError(err instanceof Error ? err.message : 'Failed to save draft');
    } finally {
      setBusy(null);
    }
  }, [draftText, handleError, jsonHeaders, policyKey, router, selectedPreset]);

  const handlePublish = useCallback(async () => {
    setBusy('publish');
    try {
      const res = await fetch(
        `/api-proxy/guardrails/tenant/${encodeURIComponent(policyKey)}/publish`,
        {
          method: 'POST',
          cache: 'no-store',
        },
      );
      if (!res.ok) {
        throw new Error(`Publish failed (${res.status})`);
      }
      setSnackbar('Guardrail published');
      router.refresh();
    } catch (err) {
      handleError(err instanceof Error ? err.message : 'Failed to publish guardrail');
    } finally {
      setBusy(null);
    }
  }, [handleError, policyKey, router]);

  const handleReset = useCallback(async () => {
    if (!selectedPreset) {
      handleError('Select a preset before resetting the policy.');
      return;
    }
    setBusy('reset');
    try {
      const res = await fetch(
        `/api-proxy/guardrails/tenant/${encodeURIComponent(policyKey)}/reset`,
        {
          method: 'POST',
          headers: jsonHeaders,
          cache: 'no-store',
          body: JSON.stringify({ presetId: selectedPreset }),
        },
      );
      if (!res.ok) {
        throw new Error(`Preset reset failed (${res.status})`);
      }
      setSnackbar('Guardrail reset to preset');
      router.refresh();
    } catch (err) {
      handleError(err instanceof Error ? err.message : 'Failed to reset policy');
    } finally {
      setBusy(null);
    }
  }, [handleError, jsonHeaders, policyKey, router, selectedPreset]);

  const disabled = busy !== null;

  return (
    <Stack spacing={3} sx={{ p: { xs: 2, md: 4 } }}>
      <Stack spacing={1}>
        <Typography variant="h4">Tenant Guardrails</Typography>
        <Typography variant="body1" color="text.secondary">
          Review the active policy, stage edits as a draft, and publish once ready. Resetting to a
          preset immediately replaces the active policy.
        </Typography>
      </Stack>

      <Grid container spacing={3} alignItems="stretch">
        <Grid item xs={12} md={5}>
          <SnapshotSummary snapshot={summary.snapshot} />
        </Grid>
        <Grid item xs={12} md={7}>
          <Card variant="outlined" sx={{ height: '100%' }}>
            <CardContent sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
              <Typography variant="h6">Draft Editor</Typography>
              <TextField
                label="Draft Definition"
                multiline
                minRows={12}
                value={draftText}
                onChange={(event) => setDraftText(event.target.value)}
                disabled={disabled}
                InputProps={{ sx: { fontFamily: 'monospace' } }}
                helperText="Edit allow/deny/escalate lists as JSON."
              />
              {parsedDraft === null && (
                <Alert severity="error">
                  Draft JSON is invalid. Fix parsing errors before saving.
                </Alert>
              )}
              <Stack
                direction={{ xs: 'column', sm: 'row' }}
                spacing={2}
                alignItems={{ xs: 'stretch', sm: 'center' }}
              >
                <Box sx={{ width: { xs: '100%', sm: 240 } }}>
                  <Typography variant="subtitle2" color="text.secondary" gutterBottom>
                    Derived preset (optional)
                  </Typography>
                  <Select
                    fullWidth
                    size="small"
                    value={selectedPreset}
                    onChange={(event) => setSelectedPreset(event.target.value)}
                    displayEmpty
                    disabled={disabled}
                    inputProps={{ 'aria-label': 'Derived preset' }}
                  >
                    <MenuItem value="">
                      <em>None</em>
                    </MenuItem>
                    {summary.presets.map(renderPresetOption)}
                  </Select>
                </Box>
                <Stack
                  direction={{ xs: 'column', sm: 'row' }}
                  spacing={2}
                  flexWrap="wrap"
                  useFlexGap
                >
                  <Button
                    variant="outlined"
                    onClick={handleReset}
                    disabled={disabled || !selectedPreset}
                    color="warning"
                    startIcon={busy === 'reset' ? <CircularProgress size={16} /> : undefined}
                  >
                    Reset to preset
                  </Button>
                  <Button
                    variant="outlined"
                    onClick={handleSaveDraft}
                    disabled={disabled || parsedDraft === null}
                    startIcon={busy === 'save' ? <CircularProgress size={16} /> : undefined}
                  >
                    Save draft
                  </Button>
                  <Button
                    variant="contained"
                    onClick={handlePublish}
                    disabled={disabled}
                    startIcon={busy === 'publish' ? <CircularProgress size={16} /> : undefined}
                  >
                    Publish
                  </Button>
                </Stack>
              </Stack>
            </CardContent>
          </Card>
        </Grid>
      </Grid>

      <Card variant="outlined">
        <CardContent>
          <Typography variant="h6" gutterBottom>
            Draft vs Active Policy
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            Additions are highlighted in green, removals in amber. Shared items remain neutral.
          </Typography>
          <Grid container spacing={2}>
            <Grid item xs={12} md={4}>
              {policyRow('Allow', diff.allow)}
            </Grid>
            <Grid item xs={12} md={4}>
              {policyRow('Deny', diff.deny)}
            </Grid>
            <Grid item xs={12} md={4}>
              {policyRow('Escalate', diff.escalate)}
            </Grid>
          </Grid>
        </CardContent>
      </Card>

      <PolicyMeta basePolicy={basePolicy} draftPolicy={draftPolicy} />

      <Snackbar
        open={Boolean(snackbar)}
        autoHideDuration={4000}
        onClose={() => setSnackbar(null)}
        message={snackbar ?? ''}
      />
      <Snackbar
        open={Boolean(error)}
        autoHideDuration={6000}
        onClose={() => setError(null)}
        message={error ?? ''}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      />
    </Stack>
  );
}

function renderPresetOption(preset: GuardrailPreset) {
  return (
    <MenuItem key={preset.id} value={preset.id}>
      <Stack spacing={0.5}>
        <Typography variant="body2" fontWeight={600}>
          {preset.name}
        </Typography>
        {preset.notes && (
          <Typography variant="caption" color="text.secondary">
            {preset.notes}
          </Typography>
        )}
      </Stack>
    </MenuItem>
  );
}

function PolicyMeta({
  basePolicy,
  draftPolicy,
}: {
  basePolicy?: TenantGuardrailPolicy;
  draftPolicy?: TenantGuardrailPolicy;
}) {
  if (!basePolicy && !draftPolicy) return null;

  const formatDate = (value?: string | null) => (value ? new Date(value).toLocaleString() : '—');

  return (
    <Card variant="outlined">
      <CardContent>
        <Typography variant="h6" gutterBottom>
          Policy Metadata
        </Typography>
        <Grid container spacing={2}>
          <Grid item xs={12} md={6}>
            <Typography variant="subtitle2" gutterBottom>
              Active policy
            </Typography>
            {basePolicy ? (
              <Stack spacing={0.5}>
                <Typography variant="body2">Version {basePolicy.version}</Typography>
                <Typography variant="body2" color="text.secondary">
                  Published {formatDate(basePolicy.publishedAt)}
                </Typography>
                <Typography variant="body2" color="text.secondary">
                  Last updated {formatDate(basePolicy.updatedAt ?? basePolicy.createdAt)}
                </Typography>
              </Stack>
            ) : (
              <Typography variant="body2" color="text.secondary">
                No active policy yet.
              </Typography>
            )}
          </Grid>
          <Grid item xs={12} md={6}>
            <Typography variant="subtitle2" gutterBottom>
              Draft details
            </Typography>
            {draftPolicy ? (
              <Stack spacing={0.5}>
                <Typography variant="body2">
                  Last updated {formatDate(draftPolicy.updatedAt)}
                </Typography>
                <Typography variant="body2" color="text.secondary">
                  Derived preset: {draftPolicy.derivedFromPresetId ?? 'None'}
                </Typography>
              </Stack>
            ) : (
              <Typography variant="body2" color="text.secondary">
                No draft saved yet.
              </Typography>
            )}
          </Grid>
        </Grid>
      </CardContent>
    </Card>
  );
}
