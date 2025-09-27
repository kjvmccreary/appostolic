'use client';

import { useCallback, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Divider,
  Grid,
  Snackbar,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import type {
  GuardrailActivityEntry,
  GuardrailDenominationPolicy,
  GuardrailDefinition,
  GuardrailSuperadminSummary,
  GuardrailSystemPolicy,
} from '../types';

const jsonHeaders = { 'content-type': 'application/json' } as const;

function stringifyDefinition(definition: GuardrailDefinition): string {
  return JSON.stringify(definition, null, 2);
}

function normalizeDefinition(text: string): GuardrailDefinition {
  const parsed = JSON.parse(text) as GuardrailDefinition;
  return parsed;
}

type SaveHandler = (message: string) => void;

type BaseEditableProps = {
  onSaved: SaveHandler;
  onError: (message: string) => void;
};

type SystemPolicyCardProps = BaseEditableProps & {
  policy: GuardrailSystemPolicy;
};

type PresetCardProps = BaseEditableProps & {
  preset: GuardrailDenominationPolicy;
};

type CreateSystemPolicyCardProps = BaseEditableProps;

type CreatePresetCardProps = BaseEditableProps;

export function GuardrailSuperAdminClient({ summary }: { summary: GuardrailSuperadminSummary }) {
  const router = useRouter();
  const [snackbar, setSnackbar] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const handleSaved = useCallback<SaveHandler>(
    (message) => {
      setError(null);
      setSnackbar(message);
      router.refresh();
    },
    [router],
  );

  return (
    <Stack spacing={4} sx={{ p: { xs: 2, md: 4 } }}>
      <Stack spacing={1}>
        <Typography variant="h4">Guardrail Platform Console</Typography>
        <Typography variant="body1" color="text.secondary">
          Manage global baselines, denomination presets, and review tenant activity. Changes are
          live immediately and will be available to tenant admins via their guardrail editor.
        </Typography>
      </Stack>

      {error ? <Alert severity="error">{error}</Alert> : null}

      <Grid container spacing={3} alignItems="stretch">
        <Grid item xs={12} md={6}>
          <Stack spacing={3}>
            <Typography variant="h5">System Policies</Typography>
            <CreateSystemPolicyCard onSaved={handleSaved} onError={setError} />
            {summary.systemPolicies.map((policy) => (
              <SystemPolicyCard
                key={policy.id}
                policy={policy}
                onSaved={handleSaved}
                onError={setError}
              />
            ))}
          </Stack>
        </Grid>
        <Grid item xs={12} md={6}>
          <Stack spacing={3}>
            <Typography variant="h5">Denomination Presets</Typography>
            <CreatePresetCard onSaved={handleSaved} onError={setError} />
            {summary.presets.map((preset) => (
              <PresetCard
                key={preset.id}
                preset={preset}
                onSaved={handleSaved}
                onError={setError}
              />
            ))}
          </Stack>
        </Grid>
      </Grid>

      <ActivityFeed activity={summary.activity} />

      <Snackbar
        open={Boolean(snackbar)}
        autoHideDuration={4000}
        onClose={() => setSnackbar(null)}
        message={snackbar ?? ''}
      />
    </Stack>
  );
}

function SystemPolicyCard({ policy, onSaved, onError }: SystemPolicyCardProps) {
  const [name, setName] = useState(policy.name);
  const [description, setDescription] = useState(policy.description ?? '');
  const [definitionText, setDefinitionText] = useState(() =>
    stringifyDefinition(policy.definition),
  );
  const [busy, setBusy] = useState(false);

  const handleSave = useCallback(async () => {
    let definition: GuardrailDefinition;
    try {
      definition = normalizeDefinition(definitionText);
    } catch {
      onError('Definition must be valid JSON.');
      return;
    }

    setBusy(true);
    try {
      const res = await fetch(
        `/api-proxy/guardrails/super/system/${encodeURIComponent(policy.slug)}`,
        {
          method: 'PUT',
          headers: jsonHeaders,
          cache: 'no-store',
          body: JSON.stringify({
            name,
            description,
            definition,
          }),
        },
      );
      if (!res.ok) {
        throw new Error(`Failed to save system policy (${res.status})`);
      }
      onSaved('System policy saved');
    } catch (err) {
      onError(err instanceof Error ? err.message : 'Failed to save system policy');
    } finally {
      setBusy(false);
    }
  }, [definitionText, description, name, onError, onSaved, policy.slug]);

  const created = useMemo(() => new Date(policy.createdAt).toLocaleString(), [policy.createdAt]);
  const updated = useMemo(
    () => (policy.updatedAt ? new Date(policy.updatedAt).toLocaleString() : null),
    [policy.updatedAt],
  );

  return (
    <Card variant="outlined" data-testid={`system-policy-card-${policy.slug}`}>
      <CardContent sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
        <Stack direction="row" justifyContent="space-between" alignItems="flex-start">
          <Stack spacing={0.5}>
            <Typography variant="h6">{policy.name}</Typography>
            <Typography variant="caption" color="text.secondary">
              {policy.slug}
            </Typography>
          </Stack>
          <Chip label={`v${policy.version}`} size="small" />
        </Stack>
        <TextField
          label="Display Name"
          value={name}
          onChange={(event) => setName(event.target.value)}
          disabled={busy}
          fullWidth
        />
        <TextField
          label="Description"
          value={description}
          onChange={(event) => setDescription(event.target.value)}
          disabled={busy}
          fullWidth
          multiline
          minRows={2}
        />
        <TextField
          label="Definition"
          value={definitionText}
          onChange={(event) => setDefinitionText(event.target.value)}
          disabled={busy}
          multiline
          minRows={10}
          InputProps={{ sx: { fontFamily: 'monospace' } }}
        />
        <Stack direction="row" justifyContent="space-between" alignItems="center" spacing={2}>
          <Typography variant="caption" color="text.secondary">
            Created {created}
            {updated ? ` • Updated ${updated}` : ''}
          </Typography>
          <Button variant="contained" onClick={handleSave} disabled={busy}>
            Save
          </Button>
        </Stack>
      </CardContent>
    </Card>
  );
}

function PresetCard({ preset, onSaved, onError }: PresetCardProps) {
  const [name, setName] = useState(preset.name);
  const [notes, setNotes] = useState(preset.notes ?? '');
  const [definitionText, setDefinitionText] = useState(() =>
    stringifyDefinition(preset.definition),
  );
  const [busy, setBusy] = useState(false);

  const handleSave = useCallback(async () => {
    let definition: GuardrailDefinition;
    try {
      definition = normalizeDefinition(definitionText);
    } catch {
      onError('Definition must be valid JSON.');
      return;
    }

    setBusy(true);
    try {
      const res = await fetch(
        `/api-proxy/guardrails/super/presets/${encodeURIComponent(preset.id)}`,
        {
          method: 'PUT',
          headers: jsonHeaders,
          cache: 'no-store',
          body: JSON.stringify({
            name,
            notes,
            definition,
          }),
        },
      );
      if (!res.ok) {
        throw new Error(`Failed to save preset (${res.status})`);
      }
      onSaved('Preset saved');
    } catch (err) {
      onError(err instanceof Error ? err.message : 'Failed to save preset');
    } finally {
      setBusy(false);
    }
  }, [definitionText, name, notes, onError, onSaved, preset.id]);

  const created = useMemo(() => new Date(preset.createdAt).toLocaleString(), [preset.createdAt]);
  const updated = useMemo(
    () => (preset.updatedAt ? new Date(preset.updatedAt).toLocaleString() : null),
    [preset.updatedAt],
  );

  return (
    <Card variant="outlined" data-testid={`preset-card-${preset.id}`}>
      <CardContent sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
        <Stack direction="row" justifyContent="space-between" alignItems="flex-start">
          <Stack spacing={0.5}>
            <Typography variant="h6">{preset.name}</Typography>
            <Typography variant="caption" color="text.secondary">
              {preset.id}
            </Typography>
          </Stack>
          <Chip label={`v${preset.version}`} size="small" />
        </Stack>
        <TextField
          label="Display Name"
          value={name}
          onChange={(event) => setName(event.target.value)}
          disabled={busy}
          fullWidth
        />
        <TextField
          label="Notes"
          value={notes}
          onChange={(event) => setNotes(event.target.value)}
          disabled={busy}
          fullWidth
          multiline
          minRows={2}
        />
        <TextField
          label="Definition"
          value={definitionText}
          onChange={(event) => setDefinitionText(event.target.value)}
          disabled={busy}
          multiline
          minRows={10}
          InputProps={{ sx: { fontFamily: 'monospace' } }}
        />
        <Stack direction="row" justifyContent="space-between" alignItems="center" spacing={2}>
          <Typography variant="caption" color="text.secondary">
            Created {created}
            {updated ? ` • Updated ${updated}` : ''}
          </Typography>
          <Button variant="contained" onClick={handleSave} disabled={busy}>
            Save
          </Button>
        </Stack>
      </CardContent>
    </Card>
  );
}

function CreateSystemPolicyCard({ onSaved, onError }: CreateSystemPolicyCardProps) {
  const [slug, setSlug] = useState('');
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [definitionText, setDefinitionText] = useState(() =>
    stringifyDefinition({ allow: [], deny: [], escalate: [] }),
  );
  const [busy, setBusy] = useState(false);

  const handleCreate = useCallback(async () => {
    if (!slug.trim()) {
      onError('Provide a slug before creating a system policy.');
      return;
    }

    let definition: GuardrailDefinition;
    try {
      definition = normalizeDefinition(definitionText);
    } catch {
      onError('Definition must be valid JSON.');
      return;
    }

    setBusy(true);
    try {
      const normalizedSlug = slug.trim().toLowerCase();
      const res = await fetch(
        `/api-proxy/guardrails/super/system/${encodeURIComponent(normalizedSlug)}`,
        {
          method: 'PUT',
          headers: jsonHeaders,
          cache: 'no-store',
          body: JSON.stringify({
            name: name || normalizedSlug,
            description,
            definition,
          }),
        },
      );
      if (!res.ok) {
        throw new Error(`Failed to create system policy (${res.status})`);
      }
      setSlug('');
      setName('');
      setDescription('');
      setDefinitionText(stringifyDefinition({ allow: [], deny: [], escalate: [] }));
      onSaved('System policy created');
    } catch (err) {
      onError(err instanceof Error ? err.message : 'Failed to create system policy');
    } finally {
      setBusy(false);
    }
  }, [definitionText, description, name, onError, onSaved, slug]);

  return (
    <Card variant="outlined" data-testid="create-system-policy">
      <CardContent sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
        <Typography variant="subtitle1" fontWeight={600}>
          Add System Policy
        </Typography>
        <TextField
          label="Slug"
          value={slug}
          onChange={(event) => setSlug(event.target.value)}
          disabled={busy}
          helperText="Lowercase identifier (e.g., system-core)"
        />
        <TextField
          label="Display Name"
          value={name}
          onChange={(event) => setName(event.target.value)}
          disabled={busy}
        />
        <TextField
          label="Description"
          value={description}
          onChange={(event) => setDescription(event.target.value)}
          disabled={busy}
          multiline
          minRows={2}
        />
        <TextField
          label="Definition"
          value={definitionText}
          onChange={(event) => setDefinitionText(event.target.value)}
          disabled={busy}
          multiline
          minRows={6}
          InputProps={{ sx: { fontFamily: 'monospace' } }}
        />
        <Box textAlign="right">
          <Button variant="contained" onClick={handleCreate} disabled={busy}>
            Add Policy
          </Button>
        </Box>
      </CardContent>
    </Card>
  );
}

function CreatePresetCard({ onSaved, onError }: CreatePresetCardProps) {
  const [presetId, setPresetId] = useState('');
  const [name, setName] = useState('');
  const [notes, setNotes] = useState('');
  const [definitionText, setDefinitionText] = useState(() =>
    stringifyDefinition({ allow: [], deny: [], escalate: [] }),
  );
  const [busy, setBusy] = useState(false);

  const handleCreate = useCallback(async () => {
    if (!presetId.trim()) {
      onError('Provide a preset id before creating a preset.');
      return;
    }

    let definition: GuardrailDefinition;
    try {
      definition = normalizeDefinition(definitionText);
    } catch {
      onError('Definition must be valid JSON.');
      return;
    }

    setBusy(true);
    try {
      const normalizedId = presetId.trim();
      const res = await fetch(
        `/api-proxy/guardrails/super/presets/${encodeURIComponent(normalizedId)}`,
        {
          method: 'PUT',
          headers: jsonHeaders,
          cache: 'no-store',
          body: JSON.stringify({
            name: name || normalizedId,
            notes,
            definition,
          }),
        },
      );
      if (!res.ok) {
        throw new Error(`Failed to create preset (${res.status})`);
      }
      setPresetId('');
      setName('');
      setNotes('');
      setDefinitionText(stringifyDefinition({ allow: [], deny: [], escalate: [] }));
      onSaved('Preset created');
    } catch (err) {
      onError(err instanceof Error ? err.message : 'Failed to create preset');
    } finally {
      setBusy(false);
    }
  }, [definitionText, name, notes, onError, onSaved, presetId]);

  return (
    <Card variant="outlined" data-testid="create-preset">
      <CardContent sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
        <Typography variant="subtitle1" fontWeight={600}>
          Add Denomination Preset
        </Typography>
        <TextField
          label="Preset Id"
          value={presetId}
          onChange={(event) => setPresetId(event.target.value)}
          disabled={busy}
          helperText="Identifier (e.g., preset-core)"
        />
        <TextField
          label="Display Name"
          value={name}
          onChange={(event) => setName(event.target.value)}
          disabled={busy}
        />
        <TextField
          label="Notes"
          value={notes}
          onChange={(event) => setNotes(event.target.value)}
          disabled={busy}
          multiline
          minRows={2}
        />
        <TextField
          label="Definition"
          value={definitionText}
          onChange={(event) => setDefinitionText(event.target.value)}
          disabled={busy}
          multiline
          minRows={6}
          InputProps={{ sx: { fontFamily: 'monospace' } }}
        />
        <Box textAlign="right">
          <Button variant="contained" onClick={handleCreate} disabled={busy}>
            Add Preset
          </Button>
        </Box>
      </CardContent>
    </Card>
  );
}

function ActivityFeed({ activity }: { activity: GuardrailActivityEntry[] }) {
  return (
    <Card variant="outlined" data-testid="activity-feed">
      <CardContent>
        <Typography variant="h5" gutterBottom>
          Recent Tenant Activity
        </Typography>
        {activity.length === 0 ? (
          <Typography variant="body2" color="text.secondary">
            No recent activity.
          </Typography>
        ) : (
          <Stack spacing={2} divider={<Divider flexItem />}>
            {activity.map((entry) => (
              <Stack key={`${entry.policyId}-${entry.occurredAt}-${entry.action}`} spacing={1}>
                <Stack direction="row" justifyContent="space-between" alignItems="center">
                  <Typography variant="subtitle1" fontWeight={600}>
                    {entry.tenantName ?? entry.tenantId}
                  </Typography>
                  <Chip label={entry.action.replace('_', ' ')} size="small" color="info" />
                </Stack>
                <Typography variant="body2" color="text.secondary">
                  Policy <strong>{entry.key}</strong> ({entry.layer}) • v{entry.version}
                </Typography>
                <Typography variant="body2" color="text.secondary">
                  {new Date(entry.occurredAt).toLocaleString()}
                  {entry.updatedByEmail ? ` • ${entry.updatedByEmail}` : ''}
                </Typography>
                {entry.derivedFromPresetId ? (
                  <Typography variant="body2" color="text.secondary">
                    Preset: {entry.derivedFromPresetId}
                  </Typography>
                ) : null}
              </Stack>
            ))}
          </Stack>
        )}
      </CardContent>
    </Card>
  );
}
