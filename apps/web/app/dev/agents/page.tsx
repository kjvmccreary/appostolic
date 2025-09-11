import { headers } from 'next/headers';
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
  const hdrs = headers();
  const host = hdrs.get('host');
  const proto = process.env.NODE_ENV === 'development' ? 'http' : 'https';
  const base = `${proto}://${host}`;

  const res = await fetch(`${base}/api-proxy/dev/agents`, { cache: 'no-store' });
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
