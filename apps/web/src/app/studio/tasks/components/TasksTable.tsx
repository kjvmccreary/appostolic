'use client';

import Link from 'next/link';
import { useMemo } from 'react';

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

function StatusBadge({ status }: { status: string }) {
  const color =
    status === 'Succeeded'
      ? 'bg-green-100 text-green-800'
      : status === 'Failed'
        ? 'bg-red-100 text-red-800'
        : status === 'Running'
          ? 'bg-blue-100 text-blue-800'
          : status === 'Canceled'
            ? 'bg-gray-200 text-gray-700'
            : 'bg-yellow-100 text-yellow-800';
  return <span className={`px-2 py-0.5 rounded text-xs font-medium ${color}`}>{status}</span>;
}

export function TasksTable({
  items,
  agentNameById,
}: {
  items: TaskSummary[];
  agentNameById: Record<string, string>;
}) {
  const rows = useMemo(() => items, [items]);

  if (!rows.length) {
    return (
      <div className="p-8 text-center border rounded-md bg-white/50">
        <p className="text-gray-700">No tasks match your filters.</p>
      </div>
    );
  }

  return (
    <div className="overflow-x-auto border rounded-md">
      <table className="min-w-full text-sm">
        <thead className="bg-gray-50 text-gray-700">
          <tr>
            <th className="text-left px-3 py-2">Status</th>
            <th className="text-left px-3 py-2">Agent</th>
            <th className="text-left px-3 py-2">Created</th>
            <th className="text-left px-3 py-2">Started</th>
            <th className="text-left px-3 py-2">Finished</th>
            <th className="text-right px-3 py-2">Total Tokens</th>
            <th className="text-right px-3 py-2">Est. Cost</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((t) => (
            <tr key={t.id} className="border-t hover:bg-gray-50">
              <td className="px-3 py-2">
                <Link href={`/studio/tasks/${t.id}`} className="underline-offset-2 hover:underline">
                  <StatusBadge status={t.status} />
                </Link>
              </td>
              <td className="px-3 py-2">
                <Link href={`/studio/tasks/${t.id}`} className="text-blue-700 hover:underline">
                  {agentNameById[t.agentId] ?? t.agentId}
                </Link>
              </td>
              <td className="px-3 py-2">{fmtDate(t.createdAt)}</td>
              <td className="px-3 py-2">{fmtDate(t.startedAt)}</td>
              <td className="px-3 py-2">{fmtDate(t.finishedAt)}</td>
              <td className="px-3 py-2 text-right">{t.totalTokens ?? '—'}</td>
              <td className="px-3 py-2 text-right">
                {t.estimatedCostUsd != null ? `$${t.estimatedCostUsd.toFixed(2)}` : '—'}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
