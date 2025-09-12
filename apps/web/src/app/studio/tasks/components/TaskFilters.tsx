'use client';

import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { useCallback, useMemo, useState } from 'react';

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
  const take = Number(params.get('take') ?? '20');
  const skip = Number(params.get('skip') ?? '0');
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
    <div className="border rounded-md p-3 bg-white/50 space-y-3">
      <div className="flex flex-wrap gap-3 items-end">
        <div>
          <label className="block text-xs text-gray-600">Status</label>
          <div className="flex flex-wrap gap-2">
            {ALL_STATUSES.map((s) => (
              <button
                key={s}
                onClick={() => toggleStatus(s)}
                className={`px-2 py-1 rounded border text-xs ${selectedStatuses.includes(s) ? 'bg-blue-600 text-white' : 'bg-white hover:bg-gray-50'}`}
                type="button"
              >
                {s}
              </button>
            ))}
          </div>
        </div>

        <div>
          <label htmlFor="agent-select" className="block text-xs text-gray-600">
            Agent
          </label>
          <select
            id="agent-select"
            title="Agent"
            className="border rounded px-2 py-1"
            value={selectedAgent}
            onChange={(e) => apply({ agentId: e.target.value || undefined })}
          >
            <option value="">All agents</option>
            {agents.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label htmlFor="from-input" className="block text-xs text-gray-600">
            From
          </label>
          <input
            type="datetime-local"
            id="from-input"
            title="From"
            className="border rounded px-2 py-1"
            value={from}
            onChange={(e) => apply({ from: e.target.value || undefined })}
          />
        </div>

        <div>
          <label htmlFor="to-input" className="block text-xs text-gray-600">
            To
          </label>
          <input
            type="datetime-local"
            id="to-input"
            title="To"
            className="border rounded px-2 py-1"
            value={to}
            onChange={(e) => apply({ to: e.target.value || undefined })}
          />
        </div>

        <div className="flex-1 min-w-[200px]">
          <label className="block text-xs text-gray-600">Search</label>
          <input
            type="text"
            className="w-full border rounded px-2 py-1"
            placeholder="Id, user, or input text"
            value={q}
            onChange={(e) => setQ(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') apply({ q });
            }}
            onBlur={() => apply({ q })}
          />
        </div>
      </div>

      <div className="flex items-center justify-between pt-2 border-t mt-2">
        <div className="space-x-2">
          <button
            className="px-2 py-1 border rounded disabled:opacity-50"
            disabled={skip <= 0}
            onClick={() => apply({ skip: String(Math.max(0, skip - take)) })}
          >
            Prev
          </button>
          <button
            className="px-2 py-1 border rounded"
            onClick={() => apply({ skip: String(skip + take) })}
          >
            Next
          </button>
        </div>
        <div className="space-x-2">
          <label htmlFor="page-size" className="text-xs text-gray-600">
            Page size
          </label>
          <select
            id="page-size"
            title="Page size"
            className="border rounded px-2 py-1"
            value={String(take)}
            onChange={(e) => apply({ take: e.target.value, skip: '0' })}
          >
            {['10', '20', '50', '100'].map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
        </div>
      </div>
    </div>
  );
}
