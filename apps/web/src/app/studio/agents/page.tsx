import Link from 'next/link';
import { AgentsTable, type AgentListItem } from './components/AgentsTable';

async function fetchAgents(): Promise<AgentListItem[]> {
  const res = await fetch(`${process.env.NEXT_PUBLIC_WEB_BASE ?? ''}/api-proxy/agents?take=50`, {
    cache: 'no-store',
    // Force server-side fetch regardless of client navigation
    next: { revalidate: 0 },
  });
  if (!res.ok) throw new Error(`Failed to load agents: ${res.status}`);
  return res.json();
}

export default async function Page() {
  const items = await fetchAgents();
  return (
    <div className="p-6 space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold">Agents</h1>
        <Link
          href="/studio/agents/new"
          className="px-3 py-2 rounded bg-blue-600 text-white hover:bg-blue-700"
        >
          New Agent
        </Link>
      </div>
      <AgentsTable items={items} />
    </div>
  );
}
