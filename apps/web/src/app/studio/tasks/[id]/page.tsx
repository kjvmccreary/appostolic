import TaskDetail from '../components/TaskDetail';
import { fetchFromProxy } from '../../../../../app/lib/serverFetch';
import { redirect } from 'next/navigation';
import type { Task, Trace } from '../types';

export const dynamic = 'force-dynamic';

async function fetchTaskWithTraces(id: string): Promise<{ task: Task; traces: Trace[] }> {
  const res = await fetchFromProxy(`/api-proxy/agent-tasks/${id}?includeTraces=true`);
  if (res.status === 401) {
    redirect('/select-tenant');
  }
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
