'use client';

import Link from 'next/link';
import { useMemo } from 'react';
import { DataGridPremium, GridColDef } from '@mui/x-data-grid-premium';
import { Box, Button } from '@mui/material';

export type AgentListItem = {
  id: string;
  name: string;
  model: string;
  temperature: number;
  maxSteps: number;
  createdAt: string;
  updatedAt?: string | null;
};

function timeAgo(iso?: string | null) {
  if (!iso) return '—';
  const d = new Date(iso);
  const diff = Date.now() - d.getTime();
  const sec = Math.floor(diff / 1000);
  if (sec < 60) return `${sec}s ago`;
  const min = Math.floor(sec / 60);
  if (min < 60) return `${min}m ago`;
  const hrs = Math.floor(min / 60);
  if (hrs < 24) return `${hrs}h ago`;
  const days = Math.floor(hrs / 24);
  return `${days}d ago`;
}

export function AgentsTable({ items }: { items: AgentListItem[] }) {
  const rows = useMemo(() => items, [items]);

  if (!rows.length) {
    return (
      <Box
        sx={{
          p: 4,
          textAlign: 'center',
          borderRadius: 1,
          border: '1px solid',
          borderColor: 'divider',
          bgcolor: 'background.paper',
        }}
      >
        <Box sx={{ mb: 2 }}>No agents yet.</Box>
        <Link href="/studio/agents/new" passHref legacyBehavior>
          <Button component="a" variant="contained">
            New Agent
          </Button>
        </Link>
      </Box>
    );
  }

  const columns: GridColDef<AgentListItem>[] = [
    {
      field: 'name',
      headerName: 'Name',
      flex: 1,
      minWidth: 180,
      renderCell: (p) => <Link href={`/studio/agents/${p.row.id}`}>{p.value as string}</Link>,
    },
    { field: 'model', headerName: 'Model', minWidth: 160 },
    {
      field: 'temperature',
      headerName: 'Temp',
      type: 'number',
      minWidth: 100,
      valueGetter: (p) => (p.value as number).toFixed(2),
    },
    { field: 'maxSteps', headerName: 'MaxSteps', type: 'number', minWidth: 110 },
    {
      field: 'updatedAt',
      headerName: 'Updated',
      minWidth: 140,
      valueGetter: (p) => timeAgo((p.value as string) ?? p.row.createdAt),
    },
    {
      field: 'actions',
      headerName: 'Actions',
      minWidth: 180,
      sortable: false,
      filterable: false,
      align: 'right',
      headerAlign: 'right',
      renderCell: (p) => (
        <Box sx={{ display: 'flex', gap: 1 }}>
          <Link href={`/studio/agents/${p.row.id}`} passHref legacyBehavior>
            <Button component="a" size="small">
              Edit
            </Button>
          </Link>
          <Link href={`/studio/agents/${p.row.id}/delete`} passHref legacyBehavior>
            <Button component="a" size="small" color="error">
              Delete
            </Button>
          </Link>
        </Box>
      ),
    },
  ];

  return (
    <Box sx={{ height: 560, width: '100%' }}>
      <DataGridPremium
        rows={rows}
        getRowId={(r) => r.id}
        columns={columns}
        initialState={{ pagination: { paginationModel: { pageSize: 20, page: 0 } } }}
        pageSizeOptions={[10, 20, 50]}
        disableRowSelectionOnClick
      />
    </Box>
  );
}
