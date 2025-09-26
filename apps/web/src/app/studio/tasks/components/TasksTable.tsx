'use client';

import Link from 'next/link';
import { useMemo, useCallback } from 'react';
import { DataGridPremium, GridColDef, GridPaginationModel } from '@mui/x-data-grid-premium';
import { Chip, Box, IconButton, Tooltip } from '@mui/material';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';

export type TaskSummary = {
  id: string;
  agentId: string;
  status: 'Pending' | 'Running' | 'Succeeded' | 'Failed' | 'Canceled' | string;
  createdAt: string;
  startedAt?: string | null;
  finishedAt?: string | null;
  totalTokens?: number | null;
  estimatedCostUsd?: number | null;
};

function fmtDate(iso?: string | null) {
  if (!iso) return '—';
  const d = new Date(iso);
  return d.toLocaleString();
}

// MUI Chip will represent the status; removed old StatusBadge

type TasksTableProps = {
  items: TaskSummary[];
  agentNameById: Record<string, string>;
  total?: number;
  take?: number;
  skip?: number;
};

export function TasksTable({ items, agentNameById, total, take, skip }: TasksTableProps) {
  const rows = useMemo(() => items, [items]);
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const paramsString = params?.toString() ?? '';

  const columns = useMemo<GridColDef<TaskSummary>[]>(
    () => [
      {
        field: 'id',
        headerName: 'ID',
        minWidth: 220,
        renderCell: (p) => (
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <Link href={`/studio/tasks/${(p.row as TaskSummary).id}`}>
              <Box
                component="span"
                sx={{ fontFamily: 'monospace', bgcolor: 'action.hover', px: 1, borderRadius: 0.5 }}
                title={String(p.value ?? '')}
              >
                {String(p.value ?? '')}
              </Box>
            </Link>
            <Tooltip title="Copy Task ID">
              <IconButton
                size="small"
                aria-label={`copy task id ${(p.row as TaskSummary).id}`}
                onClick={async (e) => {
                  e.preventDefault();
                  e.stopPropagation();
                  try {
                    await navigator.clipboard.writeText(String(p.value ?? ''));
                  } catch {
                    // ignore in grid context
                  }
                }}
              >
                <ContentCopyIcon fontSize="inherit" />
              </IconButton>
            </Tooltip>
          </Box>
        ),
      },
      {
        field: 'status',
        headerName: 'Status',
        minWidth: 140,
        renderCell: (p) => (
          <Link href={`/studio/tasks/${(p.row as TaskSummary).id}`}>
            <Chip
              size="small"
              label={String(p.value ?? '')}
              color={
                (p.value as string) === 'Succeeded'
                  ? 'success'
                  : (p.value as string) === 'Failed'
                    ? 'error'
                    : (p.value as string) === 'Running'
                      ? 'info'
                      : (p.value as string) === 'Canceled'
                        ? 'default'
                        : 'warning'
              }
              variant={(p.value as string) === 'Canceled' ? 'outlined' : 'filled'}
            />
          </Link>
        ),
      },
      {
        field: 'agentId',
        headerName: 'Agent',
        flex: 1,
        minWidth: 160,
        renderCell: (p) => (
          <Link href={`/studio/tasks/${(p.row as TaskSummary).id}`}>
            {agentNameById[p.value as string] ?? (p.value as string)}
          </Link>
        ),
      },
      {
        field: 'createdAt',
        headerName: 'Created',
        minWidth: 180,
        valueGetter: (p) => fmtDate(p.value as string),
      },
      {
        field: 'startedAt',
        headerName: 'Started',
        minWidth: 180,
        valueGetter: (p) => fmtDate(p.value as string | null),
      },
      {
        field: 'finishedAt',
        headerName: 'Finished',
        minWidth: 180,
        valueGetter: (p) => fmtDate(p.value as string | null),
      },
      {
        field: 'totalTokens',
        headerName: 'Total Tokens',
        type: 'number',
        minWidth: 140,
        align: 'right',
        headerAlign: 'right',
        valueGetter: (p) => p.value ?? '—',
      },
      {
        field: 'estimatedCostUsd',
        headerName: 'Est. Cost',
        minWidth: 120,
        align: 'right',
        headerAlign: 'right',
        valueGetter: (p) => (p.value != null ? `$${(p.value as number).toFixed(2)}` : '—'),
      },
    ],
    [agentNameById],
  );

  const paginationModel: GridPaginationModel = {
    pageSize: Math.max(1, Number(take ?? 20)),
    page: Math.floor(Number(skip ?? 0) / Math.max(1, Number(take ?? 20))),
  };

  const onPaginationModelChange = useCallback(
    (model: GridPaginationModel) => {
      const u = new URLSearchParams(paramsString);
      u.set('take', String(model.pageSize));
      u.set('skip', String(model.page * model.pageSize));
      router.push(`${pathname}?${u.toString()}`);
    },
    [paramsString, pathname, router],
  );

  if (!rows.length) {
    return (
      <Box
        sx={{
          p: 3,
          textAlign: 'center',
          borderRadius: 1,
          border: '1px solid',
          borderColor: 'divider',
          bgcolor: 'background.paper',
        }}
      >
        No tasks match your filters.
      </Box>
    );
  }

  return (
    <Box sx={{ height: 560, width: '100%' }}>
      <DataGridPremium
        rows={rows}
        getRowId={(r) => r.id}
        columns={columns}
        pagination
        paginationMode="server"
        rowCount={total ?? rows.length}
        paginationModel={paginationModel}
        onPaginationModelChange={onPaginationModelChange}
        disableRowSelectionOnClick
      />
    </Box>
  );
}
