import AgentForm, { type ToolItem } from '../components/AgentForm';
import { fetchFromProxy } from '../../../../../app/lib/serverFetch';

async function loadTools(): Promise<ToolItem[]> {
  const res = await fetchFromProxy('/api-proxy/agents/tools');
  if (!res.ok) throw new Error(`Failed to load tool catalog: ${res.status}`);
  return res.json();
}

export default async function Page() {
  const tools = await loadTools();
  return (
    <div className="p-6 space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold">New Agent</h1>
        <a className="px-3 py-2 rounded border text-sm" href="/dev/agents">
          Run panel
        </a>
      </div>
      <AgentForm mode="create" tools={tools} />
    </div>
  );
}
