'use client';

import { useState, useMemo } from 'react';
import { DataGridPremium, GridColDef } from '@mui/x-data-grid-premium';
import { Box, Button, Collapse, Grid } from '@mui/material';

type Trace = {
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

export default function TracesTable({ traces }: { traces: Trace[] }) {
  const rows = useMemo(() => traces.map((t, i) => ({ id: i, ...t })), [traces]);
  const [open, setOpen] = useState<Record<number, boolean>>({});

  const columns: GridColDef<Trace & { id: number }>[] = [
    { field: 'stepNumber', headerName: '#', minWidth: 70 },
    { field: 'kind', headerName: 'Kind', minWidth: 120 },
    { field: 'name', headerName: 'Name', flex: 1, minWidth: 160 },
    { field: 'durationMs', headerName: 'Duration (ms)', type: 'number', minWidth: 140 },
    {
      field: 'tokens',
      headerName: 'Tokens',
      minWidth: 150,
      valueGetter: (p) =>
        p.row.kind === 'Model' ? `${p.row.promptTokens ?? 0} / ${p.row.completionTokens ?? 0}` : '',
    },
    { field: 'error', headerName: 'Error', minWidth: 160 },
    {
      field: 'actions',
      headerName: '',
      minWidth: 120,
      sortable: false,
      filterable: false,
      renderCell: (p) => (
        <Button size="small" onClick={() => setOpen((o) => ({ ...o, [p.row.id]: !o[p.row.id] }))}>
          {open[p.row.id] ? 'Hide' : 'View'} JSON
        </Button>
      ),
    },
  ];

  return (
    <Box>
      <Box sx={{ height: 420, width: '100%' }}>
        <DataGridPremium
          rows={rows}
          columns={columns}
          disableRowSelectionOnClick
          hideFooterSelectedRowCount
        />
      </Box>
      {rows.map((r) => (
        <Collapse key={`exp-${r.id}`} in={!!open[r.id]} timeout="auto" unmountOnExit>
          <Box
            sx={{
              mt: 2,
              p: 2,
              border: '1px solid',
              borderColor: 'divider',
              borderRadius: 1,
              bgcolor: 'background.paper',
            }}
          >
            <Grid container spacing={2}>
              <Grid item xs={12} md={6}>
                <Box component="pre" sx={{ overflow: 'auto' }}>
                  <code>{JSON.stringify((r.input as object) ?? {}, null, 2)}</code>
                </Box>
              </Grid>
              <Grid item xs={12} md={6}>
                <Box component="pre" sx={{ overflow: 'auto' }}>
                  <code>{JSON.stringify((r.output as object) ?? {}, null, 2)}</code>
                </Box>
              </Grid>
            </Grid>
          </Box>
        </Collapse>
      ))}
    </Box>
  );
}
