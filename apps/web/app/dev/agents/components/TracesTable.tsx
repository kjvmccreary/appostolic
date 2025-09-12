'use client';
import { useState } from 'react';

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
  const [open, setOpen] = useState<Record<number, boolean>>({});
  return (
    <div className="overflow-x-auto">
      <table className="min-w-full divide-y divide-gray-200 text-sm">
        <thead className="bg-gray-50">
          <tr>
            <th className="px-3 py-2 text-left font-medium text-gray-700">#</th>
            <th className="px-3 py-2 text-left font-medium text-gray-700">Kind</th>
            <th className="px-3 py-2 text-left font-medium text-gray-700">Name</th>
            <th className="px-3 py-2 text-left font-medium text-gray-700">Duration (ms)</th>
            <th className="px-3 py-2 text-left font-medium text-gray-700">Tokens</th>
            <th className="px-3 py-2 text-left font-medium text-gray-700">Error</th>
            <th className="px-3 py-2" />
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-100">
          {traces.map((t, idx) => (
            <tr key={idx} className="align-top">
              <td className="px-3 py-2 font-mono">{t.stepNumber}</td>
              <td className="px-3 py-2">{t.kind}</td>
              <td className="px-3 py-2">{t.name}</td>
              <td className="px-3 py-2">{t.durationMs}</td>
              <td className="px-3 py-2">
                {t.kind === 'Model' ? `${t.promptTokens ?? 0} / ${t.completionTokens ?? 0}` : ''}
              </td>
              <td className="px-3 py-2 text-red-600">{t.error ?? ''}</td>
              <td className="px-3 py-2 text-right">
                <button
                  type="button"
                  className="rounded border px-2 py-1 text-xs"
                  onClick={() => setOpen((o) => ({ ...o, [idx]: !o[idx] }))}
                >
                  {open[idx] ? 'Hide' : 'View'} JSON
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {traces.map((t, idx) => (
        <div
          key={`exp-${idx}`}
          className={`${open[idx] ? 'block' : 'hidden'} mt-2 rounded border bg-gray-50 p-3`}
        >
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <pre className="overflow-auto rounded bg-white p-2 text-xs">
              <code>{JSON.stringify((t.input as object) ?? {}, null, 2)}</code>
            </pre>
            <pre className="overflow-auto rounded bg-white p-2 text-xs">
              <code>{JSON.stringify((t.output as object) ?? {}, null, 2)}</code>
            </pre>
          </div>
        </div>
      ))}
    </div>
  );
}
