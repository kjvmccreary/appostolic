import TaskDetail from '../components/TaskDetail';

export const dynamic = 'force-dynamic';

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

type Task = {
  id: string;
  agentId: string;
  status: 'Pending' | 'Running' | 'Succeeded' | 'Failed' | 'Canceled';
  createdAt: string;
  startedAt?: string | null;
  finishedAt?: string | null;
  totalTokens?: number | null;
  totalPromptTokens?: number | null;
  totalCompletionTokens?: number | null;
  estimatedCostUsd?: number | null;
  result?: unknown;
  error?: string | null;
};

async function fetchTaskWithTraces(id: string): Promise<{ task: Task; traces: Trace[] }> {
  const res = await fetch(`/api-proxy/agent-tasks/${id}?includeTraces=true`, {
    cache: 'no-store',
    next: { revalidate: 0 },
  });
  if (!res.ok) throw new Error(`Failed to load task ${id}: ${res.status}`);
  return (await res.json()) as { task: Task; traces: Trace[] };
}

export default async function Page({ params }: { params: { id: string } }) {
  const { id } = params;
  const { task, traces } = await fetchTaskWithTraces(id);
  return (
    <div className="p-6 space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold">Task Details</h1>
      </div>
      <TaskDetail task={task} traces={traces} />
    </div>
  );
}
