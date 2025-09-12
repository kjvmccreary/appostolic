import AgentForm, { type ToolItem } from '../components/AgentForm';

async function loadTools(): Promise<ToolItem[]> {
  const res = await fetch(`${process.env.NEXT_PUBLIC_WEB_BASE ?? ''}/api-proxy/agents/tools`, {
    cache: 'no-store',
    next: { revalidate: 0 },
  });
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
