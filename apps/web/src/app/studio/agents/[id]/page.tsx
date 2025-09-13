import AgentForm, { type ToolItem, type AgentDetails } from '../components/AgentForm';
import { fetchFromProxy } from '../../../../../app/lib/serverFetch';
import { redirect } from 'next/navigation';

async function loadTools(): Promise<ToolItem[]> {
  const res = await fetchFromProxy('/api-proxy/agents/tools');
  if (res.status === 401) {
    redirect('/select-tenant');
  }
  if (!res.ok) throw new Error(`Failed to load tool catalog: ${res.status}`);
  return res.json();
}

async function loadAgent(id: string): Promise<AgentDetails> {
  const res = await fetchFromProxy(`/api-proxy/agents/${id}`);
  if (res.status === 401) {
    redirect('/select-tenant');
  }
  if (!res.ok) throw new Error(`Failed to load agent: ${res.status}`);
  return res.json();
}

export default async function Page({ params }: { params: { id: string } }) {
  const [tools, agent] = await Promise.all([loadTools(), loadAgent(params.id)]);
  return (
    <div className="p-6 space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold">Edit Agent</h1>
        <a
          className="px-3 py-2 rounded border text-sm"
          href={`/dev/agents?agentId=${encodeURIComponent(agent.id)}`}
        >
          Run this agent
        </a>
      </div>
      <AgentForm mode="edit" tools={tools} initial={agent} />
    </div>
  );
}
