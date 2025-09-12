import AgentForm, { type ToolItem, type AgentDetails } from '../components/AgentForm';

async function loadTools(): Promise<ToolItem[]> {
  const res = await fetch(`${process.env.NEXT_PUBLIC_WEB_BASE ?? ''}/api-proxy/agents/tools`, {
    cache: 'no-store',
    next: { revalidate: 0 },
  });
  if (!res.ok) throw new Error(`Failed to load tool catalog: ${res.status}`);
  return res.json();
}

async function loadAgent(id: string): Promise<AgentDetails> {
  const res = await fetch(`${process.env.NEXT_PUBLIC_WEB_BASE ?? ''}/api-proxy/agents/${id}`, {
    cache: 'no-store',
    next: { revalidate: 0 },
  });
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
