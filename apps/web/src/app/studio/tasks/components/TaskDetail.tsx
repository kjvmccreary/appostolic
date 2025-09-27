'use client';

import { useCallback, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import {
  Box,
  Stack,
  Card,
  CardContent,
  Typography,
  Chip,
  Button,
  IconButton,
  CircularProgress,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Snackbar,
  Alert,
} from '@mui/material';
import { DataGridPremium, GridColDef, GridRenderCellParams } from '@mui/x-data-grid-premium';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import DownloadIcon from '@mui/icons-material/Download';
import CancelIcon from '@mui/icons-material/Cancel';
import ReplayIcon from '@mui/icons-material/Replay';
import { formatDistanceToNow, parseISO } from 'date-fns';
import type { GuardrailDecision, GuardrailMetadata, Task, Trace } from '../types';

// Maps guardrail decisions to chip colors so deny/escalate stand out in the UI.
function guardrailChipColor(
  decision: GuardrailDecision | null | undefined,
): 'default' | 'success' | 'warning' | 'error' {
  if (decision === 'Deny') return 'error';
  if (decision === 'Escalate') return 'warning';
  if (decision === 'Allow') return 'success';
  return 'default';
}

// Builds the alert message that explains why a guardrail intervened.
function guardrailAlertMessage(decision: GuardrailDecision, reason?: string | null): string {
  const base =
    decision === 'Deny'
      ? 'Guardrail denied this task before execution.'
      : 'Guardrail escalated this task for review.';
  return reason ? `${base} (${reason})` : base;
}

export default function TaskDetail({
  task: initialTask,
  traces: initialTraces,
}: {
  task: Task;
  traces: Trace[];
}) {
  const router = useRouter();
  const [task, setTask] = useState<Task>(initialTask);
  const [traces, setTraces] = useState<Trace[]>(initialTraces);
  const [busy, setBusy] = useState<'cancel' | 'retry' | 'export' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [snackOpen, setSnackOpen] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);

  const guardrailDecision = task.guardrailDecision ?? null;
  const guardrailMetadata: GuardrailMetadata | null = task.guardrailMetadata ?? null;
  const primaryGuardrailMatch = guardrailMetadata?.result?.matches?.[0];

  // Collates matched and input signals into a deduplicated list for display.
  const guardrailSignals = useMemo(() => {
    if (!guardrailMetadata) return [] as string[];
    const dedup = new Set<string>();
    guardrailMetadata.result?.matchedSignals?.forEach((signal) => {
      if (signal) dedup.add(signal);
    });
    guardrailMetadata.context?.signals?.forEach((signal) => {
      if (signal) dedup.add(signal);
    });
    return Array.from(dedup);
  }, [guardrailMetadata]);

  const guardrailJson = useMemo(
    () => (guardrailMetadata ? JSON.stringify(guardrailMetadata, null, 2) : null),
    [guardrailMetadata],
  );

  const guardrailReason = guardrailMetadata?.result?.reasonCode ?? null;
  const guardrailPrompt = guardrailMetadata?.context?.promptSummary ?? null;
  const guardrailChannel = guardrailMetadata?.context?.channel ?? null;

  const isTerminal =
    task.status === 'Succeeded' || task.status === 'Failed' || task.status === 'Canceled';
  const canCancel = task.status === 'Pending' || task.status === 'Running';

  const refresh = useCallback(async () => {
    try {
      const res = await fetch(`/api-proxy/agent-tasks/${task.id}?includeTraces=true`, {
        cache: 'no-store',
      });
      if (!res.ok) throw new Error(`Refresh failed: ${res.status}`);
      const data = (await res.json()) as { task: Task; traces: Trace[] };
      setTask(data.task);
      setTraces(data.traces);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Failed to refresh';
      setError(msg);
      setSnackOpen(true);
    }
  }, [task.id]);

  const onCancel = async () => {
    setConfirmOpen(false);
    try {
      setBusy('cancel');
      const res = await fetch(`/api-proxy/agent-tasks/${task.id}/cancel`, { method: 'POST' });
      if (!res.ok) throw new Error(`Cancel failed: ${res.status}`);
      await refresh();
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Cancel failed';
      setError(msg);
      setSnackOpen(true);
    } finally {
      setBusy(null);
    }
  };

  const onRetry = async () => {
    try {
      setBusy('retry');
      const res = await fetch(`/api-proxy/agent-tasks/${task.id}/retry`, { method: 'POST' });
      if (!res.ok) throw new Error(`Retry failed: ${res.status}`);
      const newTask = (await res.json()) as { id: string };
      router.push(`/studio/tasks/${newTask.id}`);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Retry failed';
      setError(msg);
      setSnackOpen(true);
    } finally {
      setBusy(null);
    }
  };

  const onExport = async () => {
    try {
      setBusy('export');
      // Prepare anchor early and set a safe fallback filename that tests can observe immediately
      const a = document.createElement('a');
      const fallbackFilename = `task-${task.id}.json`;
      a.download = fallbackFilename;
      try {
        a.setAttribute('download', fallbackFilename);
      } catch {
        // ignore
      }

      const res = await fetch(`/api-proxy/agent-tasks/${task.id}/export`);
      if (!res.ok) throw new Error(`Export failed: ${res.status}`);
      const blob = await res.blob();

      // Build a URL for the blob with a robust fallback when createObjectURL is unavailable (e.g., jsdom)
      let url: string;
      let shouldRevoke = false;
      if (typeof URL !== 'undefined' && typeof URL.createObjectURL === 'function') {
        url = URL.createObjectURL(blob);
        shouldRevoke = true;
      } else {
        const text = await blob.text();
        const mime = res.headers.get('content-type') || 'application/json';
        url = `data:${mime};charset=utf-8,${encodeURIComponent(text)}`;
      }

      const cd = res.headers.get('content-disposition') || '';
      const match = /filename\*=UTF-8''([^;]+)|filename="?([^;"]+)"?/i.exec(cd);
      const filename = decodeURIComponent(match?.[1] || match?.[2] || fallbackFilename);
      a.href = url;
      // Reaffirm the final filename (same as fallback when header is absent)
      a.download = filename;
      try {
        a.setAttribute('download', filename);
      } catch {
        // ignore
      }

      document.body.appendChild(a);
      a.click();
      a.remove();
      if (shouldRevoke) URL.revokeObjectURL(url);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Export failed';
      setError(msg);
      setSnackOpen(true);
    } finally {
      setBusy(null);
    }
  };

  const statusColor: Record<Task['status'], 'default' | 'info' | 'success' | 'error' | 'warning'> =
    {
      Pending: 'default',
      Running: 'info',
      Succeeded: 'success',
      Failed: 'error',
      Canceled: 'warning',
    };

  const rows = useMemo(() => traces.map((t) => ({ id: t.stepNumber, ...t })), [traces]);
  const columns: GridColDef<Trace & { id: number }>[] = [
    { field: 'stepNumber', headerName: '#', minWidth: 70, disableColumnMenu: true },
    {
      field: 'kind',
      headerName: 'Kind',
      minWidth: 120,
      renderCell: (p: GridRenderCellParams<Trace & { id: number }, string>) => (
        <Chip
          size="small"
          label={p.value}
          color={p.row.error ? 'error' : p.value === 'Model' ? 'info' : 'default'}
        />
      ),
    },
    { field: 'name', headerName: 'Name', flex: 1, minWidth: 160 },
    { field: 'durationMs', headerName: 'Duration (ms)', type: 'number', minWidth: 140 },
    {
      field: 'promptTokens',
      headerName: 'Prompt',
      type: 'number',
      minWidth: 100,
      valueGetter: (p) => (p.row.kind === 'Model' ? (p.row.promptTokens ?? 0) : null),
    },
    {
      field: 'completionTokens',
      headerName: 'Completion',
      type: 'number',
      minWidth: 120,
      valueGetter: (p) => (p.row.kind === 'Model' ? (p.row.completionTokens ?? 0) : null),
    },
    { field: 'error', headerName: 'Error', minWidth: 180, flex: 0.8 },
  ];

  function friendly(ts?: string | null) {
    if (!ts) return '—';
    try {
      return formatDistanceToNow(parseISO(ts), { addSuffix: true });
    } catch {
      return ts;
    }
  }

  return (
    <Stack spacing={2}>
      <Card variant="outlined">
        <CardContent>
          <Stack
            direction={{ xs: 'column', sm: 'row' }}
            spacing={2}
            justifyContent="space-between"
            alignItems={{ xs: 'flex-start', sm: 'center' }}
          >
            <Stack direction="row" spacing={1} alignItems="center">
              <Typography variant="h6">Task</Typography>
              <Typography
                variant="body2"
                component="span"
                sx={{ fontFamily: 'monospace', bgcolor: 'action.hover', px: 1, borderRadius: 0.5 }}
                aria-label="task id"
                title={task.id}
              >
                {task.id}
              </Typography>
              <IconButton
                aria-label="copy task id"
                size="small"
                onClick={async () => {
                  try {
                    await navigator.clipboard.writeText(task.id);
                  } catch (e: unknown) {
                    const msg = e instanceof Error ? e.message : 'Copy failed';
                    setError(msg);
                    setSnackOpen(true);
                  }
                }}
              >
                <ContentCopyIcon fontSize="inherit" />
              </IconButton>
              <Chip
                size="small"
                label={task.status}
                color={statusColor[task.status]}
                aria-label={`status ${task.status}`}
              />
              {guardrailDecision && (
                <Chip
                  size="small"
                  label={`Guardrail: ${guardrailDecision}`}
                  color={guardrailChipColor(guardrailDecision)}
                  variant={guardrailDecision === 'Escalate' ? 'outlined' : 'filled'}
                  aria-label={`guardrail decision ${guardrailDecision.toLowerCase()}`}
                />
              )}
            </Stack>
            {guardrailDecision && guardrailDecision !== 'Allow' && (
              <Alert severity={guardrailDecision === 'Deny' ? 'error' : 'warning'} sx={{ mt: 2 }}>
                {guardrailAlertMessage(guardrailDecision, guardrailReason)}
              </Alert>
            )}
            <Stack direction="row" spacing={1}>
              <Button
                variant="outlined"
                startIcon={busy === 'export' ? <CircularProgress size={16} /> : <DownloadIcon />}
                onClick={onExport}
                disabled={busy !== null}
                aria-label="export JSON"
              >
                Export
              </Button>
              {canCancel && (
                <Button
                  color="warning"
                  variant="outlined"
                  startIcon={busy === 'cancel' ? <CircularProgress size={16} /> : <CancelIcon />}
                  onClick={() => setConfirmOpen(true)}
                  disabled={busy !== null}
                  aria-label="cancel task"
                >
                  Cancel
                </Button>
              )}
              {isTerminal && (
                <Button
                  color="primary"
                  variant="contained"
                  startIcon={busy === 'retry' ? <CircularProgress size={16} /> : <ReplayIcon />}
                  onClick={onRetry}
                  disabled={busy !== null}
                  aria-label="retry task"
                >
                  Retry
                </Button>
              )}
            </Stack>
          </Stack>

          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} sx={{ mt: 2 }}>
            {guardrailDecision && (
              <Card variant="outlined" sx={{ flex: 1 }}>
                <CardContent>
                  <Typography variant="subtitle2" color="text.secondary">
                    Guardrail
                  </Typography>
                  <Stack spacing={1} sx={{ mt: 1 }}>
                    <Stack direction="row" spacing={1} alignItems="center">
                      <Chip
                        size="small"
                        label={guardrailDecision}
                        color={guardrailChipColor(guardrailDecision)}
                        variant={guardrailDecision === 'Escalate' ? 'outlined' : 'filled'}
                      />
                      {guardrailReason && (
                        <Typography variant="body2" color="text.secondary">
                          {guardrailReason}
                        </Typography>
                      )}
                    </Stack>
                    {guardrailPrompt && (
                      <Typography variant="body2">Prompt: “{guardrailPrompt}”</Typography>
                    )}
                    {guardrailSignals.length > 0 && (
                      <Typography variant="body2">
                        Signals: {guardrailSignals.join(', ')}
                      </Typography>
                    )}
                    {primaryGuardrailMatch?.rule && (
                      <Typography variant="body2">
                        Match: {primaryGuardrailMatch.rule}
                        {primaryGuardrailMatch.source ? ` (${primaryGuardrailMatch.source})` : ''}
                      </Typography>
                    )}
                    {primaryGuardrailMatch?.layer && (
                      <Typography variant="body2" color="text.secondary">
                        Layer: {primaryGuardrailMatch.layer}
                      </Typography>
                    )}
                    {guardrailChannel && (
                      <Typography variant="body2">Channel: {guardrailChannel}</Typography>
                    )}
                    {guardrailMetadata?.evaluatedAt && (
                      <Typography variant="body2">
                        Evaluated {friendly(guardrailMetadata.evaluatedAt)}
                      </Typography>
                    )}
                  </Stack>
                  {guardrailJson && (
                    <Box sx={{ mt: 2 }}>
                      <Stack direction="row" justifyContent="space-between" alignItems="center">
                        <Typography variant="subtitle2">Metadata</Typography>
                        <IconButton
                          size="small"
                          aria-label="copy guardrail metadata"
                          onClick={async () => {
                            try {
                              await navigator.clipboard.writeText(guardrailJson);
                            } catch (e: unknown) {
                              const msg = e instanceof Error ? e.message : 'Copy failed';
                              setError(msg);
                              setSnackOpen(true);
                            }
                          }}
                        >
                          <ContentCopyIcon fontSize="inherit" />
                        </IconButton>
                      </Stack>
                      <Box
                        component="pre"
                        sx={{
                          m: 1,
                          p: 1,
                          bgcolor: 'background.paper',
                          borderRadius: 1,
                          maxHeight: 200,
                          overflow: 'auto',
                          fontSize: '0.75rem',
                        }}
                      >
                        <code>{guardrailJson}</code>
                      </Box>
                    </Box>
                  )}
                </CardContent>
              </Card>
            )}
            <Card variant="outlined" sx={{ flex: 1 }}>
              <CardContent>
                <Typography variant="subtitle2" color="text.secondary">
                  Timestamps
                </Typography>
                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ mt: 1 }}>
                  <Typography>Created: {friendly(task.createdAt)}</Typography>
                  <Typography>Started: {friendly(task.startedAt)}</Typography>
                  <Typography>Finished: {friendly(task.finishedAt)}</Typography>
                </Stack>
              </CardContent>
            </Card>
            <Card variant="outlined" sx={{ flex: 1 }}>
              <CardContent>
                <Typography variant="subtitle2" color="text.secondary">
                  Tokens
                </Typography>
                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ mt: 1 }}>
                  <Typography>Total: {task.totalTokens ?? 0}</Typography>
                  <Typography>Prompt: {task.totalPromptTokens ?? 0}</Typography>
                  <Typography>Completion: {task.totalCompletionTokens ?? 0}</Typography>
                </Stack>
              </CardContent>
            </Card>
            <Card variant="outlined" sx={{ flex: 1 }}>
              <CardContent>
                <Typography variant="subtitle2" color="text.secondary">
                  Cost
                </Typography>
                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ mt: 1 }}>
                  <Typography>
                    Est. Cost:{' '}
                    {task.estimatedCostUsd != null
                      ? `$${(task.estimatedCostUsd as number).toFixed(4)}`
                      : '—'}
                  </Typography>
                </Stack>
              </CardContent>
            </Card>
          </Stack>
        </CardContent>
      </Card>

      <Card variant="outlined">
        <CardContent>
          <Typography variant="h6" sx={{ mb: 1 }}>
            Traces
          </Typography>
          <Box sx={{ height: 520, width: '100%' }}>
            <DataGridPremium
              rows={rows}
              columns={columns}
              initialState={{ sorting: { sortModel: [{ field: 'stepNumber', sort: 'asc' }] } }}
              getDetailPanelContent={({ row }) => (
                <Box sx={{ p: 2 }}>
                  <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
                    <Card variant="outlined" sx={{ flex: 1 }}>
                      <CardContent>
                        <Stack direction="row" justifyContent="space-between" alignItems="center">
                          <Typography variant="subtitle2">Input JSON</Typography>
                          <IconButton
                            size="small"
                            aria-label="copy input json"
                            onClick={async () => {
                              try {
                                await navigator.clipboard.writeText(
                                  JSON.stringify(row.input ?? {}, null, 2),
                                );
                              } catch (e: unknown) {
                                const msg = e instanceof Error ? e.message : 'Copy failed';
                                setError(msg);
                                setSnackOpen(true);
                              }
                            }}
                          >
                            <ContentCopyIcon fontSize="inherit" />
                          </IconButton>
                        </Stack>
                        <Box
                          component="pre"
                          sx={{ m: 1, p: 1, bgcolor: 'background.paper', overflow: 'auto' }}
                        >
                          <code>{JSON.stringify(row.input ?? {}, null, 2)}</code>
                        </Box>
                      </CardContent>
                    </Card>
                    <Card variant="outlined" sx={{ flex: 1 }}>
                      <CardContent>
                        <Stack direction="row" justifyContent="space-between" alignItems="center">
                          <Typography variant="subtitle2">Result JSON</Typography>
                          <IconButton
                            size="small"
                            aria-label="copy result json"
                            onClick={async () => {
                              try {
                                await navigator.clipboard.writeText(
                                  JSON.stringify(row.output ?? {}, null, 2),
                                );
                              } catch (e: unknown) {
                                const msg = e instanceof Error ? e.message : 'Copy failed';
                                setError(msg);
                                setSnackOpen(true);
                              }
                            }}
                          >
                            <ContentCopyIcon fontSize="inherit" />
                          </IconButton>
                        </Stack>
                        <Box
                          component="pre"
                          sx={{ m: 1, p: 1, bgcolor: 'background.paper', overflow: 'auto' }}
                        >
                          <code>{JSON.stringify(row.output ?? {}, null, 2)}</code>
                        </Box>
                      </CardContent>
                    </Card>
                  </Stack>
                </Box>
              )}
              getDetailPanelHeight={() => 260}
              disableRowSelectionOnClick
              hideFooterSelectedRowCount
            />
          </Box>
        </CardContent>
      </Card>

      <Dialog
        open={confirmOpen}
        onClose={() => setConfirmOpen(false)}
        aria-labelledby="confirm-cancel-title"
      >
        <DialogTitle id="confirm-cancel-title">Cancel task?</DialogTitle>
        <DialogContent>
          <Typography>
            Are you sure you want to cancel this task? Running tasks will stop shortly.
          </Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmOpen(false)}>No</Button>
          <Button
            color="warning"
            variant="contained"
            onClick={onCancel}
            startIcon={busy === 'cancel' ? <CircularProgress size={16} /> : <CancelIcon />}
          >
            Cancel Task
          </Button>
        </DialogActions>
      </Dialog>

      <Snackbar
        open={snackOpen}
        autoHideDuration={5000}
        onClose={() => setSnackOpen(false)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert
          onClose={() => setSnackOpen(false)}
          severity="error"
          variant="filled"
          sx={{ width: '100%' }}
        >
          {error}
        </Alert>
      </Snackbar>
    </Stack>
  );
}
