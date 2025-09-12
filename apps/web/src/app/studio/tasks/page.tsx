import { TaskFilters } from './components/TaskFilters';
import { TasksTable, type TaskSummary } from './components/TasksTable';

export const dynamic = 'force-dynamic';

type SearchParams = { [key: string]: string | string[] | undefined };

function toQueryString(sp: SearchParams) {
  const u = new URLSearchParams();
  for (const [k, v] of Object.entries(sp)) {
    if (Array.isArray(v)) v.forEach((vv) => vv && u.append(k, vv));
    else if (v != null && v !== '') u.set(k, v);
  }
  // Defaults
  if (!u.has('take')) u.set('take', '20');
  if (!u.has('skip')) u.set('skip', '0');
  return u.toString();
}

function splitStatuses(sp: SearchParams): { apiParams: SearchParams; statuses: string[] } {
  const all = ([] as string[])
    .concat(sp['status'] ?? [])
    .flatMap((v) => (Array.isArray(v) ? v : v ? [v] : []));
  if (all.length <= 1) {
    return { apiParams: sp, statuses: all };
  }
  const apiParams: SearchParams = {};
  for (const [k, v] of Object.entries(sp)) {
    if (k !== 'status') apiParams[k] = v;
  }
  return { apiParams, statuses: all };
}

async function fetchTasks(sp: SearchParams): Promise<{ items: TaskSummary[]; total?: number }> {
  const { apiParams, statuses } = splitStatuses(sp);
  const qs = toQueryString(apiParams);
  const res = await fetch(`/api-proxy/agent-tasks?${qs}`, {
    cache: 'no-store',
    next: { revalidate: 0 },
  });
  if (!res.ok) throw new Error(`Failed to load tasks: ${res.status}`);
  const totalHeader = res.headers.get('x-total-count');
  const total = totalHeader ? Number(totalHeader) : undefined;
  let items = (await res.json()) as TaskSummary[];
  // If multiple statuses were selected, apply client-side filtering over the page
  if (statuses.length > 1) {
    const set = new Set(statuses);
    items = items.filter((t) => set.has(t.status));
  }
  return { items, total };
}

type AgentListItem = { id: string; name: string };
async function fetchAgents(): Promise<AgentListItem[]> {
  const res = await fetch(`/api-proxy/agents?take=200`, {
    cache: 'no-store',
    next: { revalidate: 0 },
  });
  if (!res.ok) throw new Error(`Failed to load agents: ${res.status}`);
  return (await res.json()) as AgentListItem[];
}

export default async function Page({ searchParams }: { searchParams: SearchParams }) {
  const [{ items }, agents] = await Promise.all([fetchTasks(searchParams), fetchAgents()]);
  const agentNameById = Object.fromEntries(agents.map((a) => [a.id, a.name]));
  return (
    <div className="p-6 space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold">Tasks</h1>
      </div>
      <TaskFilters agents={agents} />
      <TasksTable items={items} agentNameById={agentNameById} />
    </div>
  );
}
