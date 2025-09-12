'use client';

import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { useCallback, useMemo, useState } from 'react';
import {
  Box,
  Button,
  Chip,
  FormControl,
  InputLabel,
  MenuItem,
  OutlinedInput,
  Select,
  TextField,
  Typography,
} from '@mui/material';
import { DateTimePicker } from '@mui/x-date-pickers/DateTimePicker';

type AgentOption = { id: string; name: string };

const ALL_STATUSES = ['Pending', 'Running', 'Succeeded', 'Failed', 'Canceled'] as const;

export function TaskFilters({ agents }: { agents: AgentOption[] }) {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();

  const [q, setQ] = useState(params.get('q') ?? '');
  const selectedAgent = params.get('agentId') ?? '';
  const from = params.get('from') ?? '';
  const to = params.get('to') ?? '';
  // pagination handled by DataGrid; no local take/skip controls here
  const selectedStatuses = useMemo(() => {
    const s = params.getAll('status');
    return s.length ? s : ([] as string[]);
  }, [params]);

  const apply = useCallback(
    (updates: Record<string, string | string[] | undefined>) => {
      const u = new URLSearchParams(params.toString());
      // Clear paging on filter changes unless explicitly set
      if (!('skip' in updates)) u.set('skip', '0');
      Object.entries(updates).forEach(([k, v]) => {
        if (Array.isArray(v)) {
          u.delete(k);
          v.forEach((vv) => u.append(k, vv));
        } else if (v == null || v === '') {
          u.delete(k);
        } else {
          u.set(k, v);
        }
      });
      router.push(`${pathname}?${u.toString()}`);
    },
    [params, pathname, router],
  );

  const toggleStatus = (s: string) => {
    const set = new Set(selectedStatuses);
    if (set.has(s)) set.delete(s);
    else set.add(s);
    apply({ status: Array.from(set) });
  };

  return (
    <Box
      sx={{
        borderRadius: 1,
        p: 2,
        border: '1px solid',
        borderColor: 'divider',
        bgcolor: 'background.paper',
      }}
    >
      <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 2, alignItems: 'flex-end' }}>
        <Box>
          <Typography variant="caption" color="text.secondary">
            Status
          </Typography>
          <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, mt: 0.5 }}>
            {ALL_STATUSES.map((s) => (
              <Chip
                key={s}
                label={s}
                size="small"
                color={selectedStatuses.includes(s) ? 'primary' : 'default'}
                variant={selectedStatuses.includes(s) ? 'filled' : 'outlined'}
                onClick={() => toggleStatus(s)}
                sx={{ cursor: 'pointer' }}
              />
            ))}
          </Box>
        </Box>

        <FormControl sx={{ minWidth: 220 }}>
          <InputLabel id="agent-select-label">Agent</InputLabel>
          <Select
            labelId="agent-select-label"
            id="agent-select"
            value={selectedAgent}
            label="Agent"
            onChange={(e) => apply({ agentId: e.target.value || undefined })}
            input={<OutlinedInput label="Agent" />}
          >
            <MenuItem value="">
              <em>All agents</em>
            </MenuItem>
            {agents.map((a) => (
              <MenuItem key={a.id} value={a.id}>
                {a.name}
              </MenuItem>
            ))}
          </Select>
        </FormControl>

        <DateTimePicker
          label="From"
          value={from ? new Date(from) : null}
          onChange={(val) => apply({ from: val ? new Date(val).toISOString() : undefined })}
          slotProps={{ textField: { sx: { minWidth: 240 } } }}
        />

        <DateTimePicker
          label="To"
          value={to ? new Date(to) : null}
          onChange={(val) => apply({ to: val ? new Date(val).toISOString() : undefined })}
          slotProps={{ textField: { sx: { minWidth: 240 } } }}
        />

        <TextField
          label="Search"
          placeholder="Id, user, or input text"
          value={q}
          onChange={(e) => setQ(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') apply({ q });
          }}
          onBlur={() => apply({ q })}
          sx={{ minWidth: 260, flex: 1 }}
        />
        <Button
          variant="outlined"
          onClick={() =>
            apply({ q: '', agentId: undefined, from: undefined, to: undefined, status: [] })
          }
        >
          Reset
        </Button>
      </Box>
    </Box>
  );
}
