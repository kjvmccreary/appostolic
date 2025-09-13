import Link from 'next/link';
import { fetchFromProxy } from '../../lib/serverFetch';
import { redirect } from 'next/navigation';
import { AgentsTable, type AgentListItem } from './components/AgentsTable';

export const dynamic = 'force-dynamic';

async function fetchAgents(): Promise<AgentListItem[]> {
  const res = await fetchFromProxy('/api-proxy/agents?take=50');
  if (res.status === 401) {
    // No session or missing tenant when WEB_AUTH_ENABLED=true → send to selector
    redirect('/select-tenant');
  }
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
