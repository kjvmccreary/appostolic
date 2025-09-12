import { fetchFromProxy } from '../../lib/serverFetch';
import AgentRunForm from './components/AgentRunForm';

type Agent = {
  id: string;
  name: string;
  model: string;
  temperature: number;
  maxSteps: number;
  toolAllowlist: string[];
};

export const dynamic = 'force-dynamic';

export default async function Page() {
  const res = await fetchFromProxy('/api-proxy/dev/agents');
  if (!res.ok) {
    throw new Error(`Failed to load agents: ${res.status}`);
  }
  const agents = (await res.json()) as Agent[];

  return (
    <main className="mx-auto max-w-3xl p-6 space-y-6">
      <h1 className="text-2xl font-semibold">Run Agent (Dev)</h1>
      <p className="text-sm text-gray-500">
        Create a task for a seeded agent and watch traces live.
      </p>
      <AgentRunForm agents={agents} />
    </main>
  );
}
