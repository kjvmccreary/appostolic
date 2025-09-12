'use client';

import Link from 'next/link';
import { useMemo } from 'react';

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
      <div className="p-8 text-center border rounded-md bg-white/50">
        <p className="text-gray-700 mb-4">No agents yet.</p>
        <Link
          href="/studio/agents/new"
          className="inline-block px-3 py-2 rounded bg-blue-600 text-white hover:bg-blue-700"
        >
          New Agent
        </Link>
      </div>
    );
  }

  return (
    <div className="overflow-x-auto border rounded-md">
      <table className="min-w-full text-sm">
        <thead className="bg-gray-50 text-gray-700">
          <tr>
            <th className="text-left px-3 py-2">Name</th>
            <th className="text-left px-3 py-2">Model</th>
            <th className="text-left px-3 py-2">Temp</th>
            <th className="text-left px-3 py-2">MaxSteps</th>
            <th className="text-left px-3 py-2">Updated</th>
            <th className="text-right px-3 py-2">Actions</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((a) => (
            <tr key={a.id} className="border-t">
              <td className="px-3 py-2 font-medium text-blue-700">
                <Link href={`/studio/agents/${a.id}`}>{a.name}</Link>
              </td>
              <td className="px-3 py-2">{a.model}</td>
              <td className="px-3 py-2">{a.temperature.toFixed(2)}</td>
              <td className="px-3 py-2">{a.maxSteps}</td>
              <td className="px-3 py-2">{timeAgo(a.updatedAt ?? a.createdAt)}</td>
              <td className="px-3 py-2 text-right space-x-2">
                <Link
                  href={`/studio/agents/${a.id}`}
                  className="px-2 py-1 border rounded hover:bg-gray-50"
                >
                  Edit
                </Link>
                <Link
                  href={`/studio/agents/${a.id}/delete`}
                  className="px-2 py-1 border rounded hover:bg-gray-50 text-red-700"
                >
                  Delete
                </Link>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
