'use client';
import { useEffect, useMemo, useState } from 'react';
import TracesTable from './TracesTable';
import { useTaskPolling } from '../../../../src/app/dev/agents/hooks/useTaskPolling';

type Agent = {
  id: string;
  name: string;
};

type Props = { agents: Agent[] };

type TaskView = {
  status: string;
  startedAt: string | null;
  finishedAt: string | null;
  errorMessage?: string | null;
};

export default function AgentRunForm({ agents }: Props) {
  const [agentId, setAgentId] = useState(agents[0]?.id ?? '');
  const [inputText, setInputText] = useState<string>(
    JSON.stringify({ topic: 'Beatitudes' }, null, 2),
  );
  const [taskId, setTaskId] = useState<string | null>(null);
  const [task, setTask] = useState<TaskView | null>(null);
  const { task: polledTask, traces, isDone, error } = useTaskPolling(taskId);
  const [isRunning, setIsRunning] = useState(false);
  const [recent, setRecent] = useState<string[]>([]);
  // polling lifecycle is handled by the hook

  async function createTask() {
    setIsRunning(true);
    setTask(null);

    let body: { agentId: string; input: unknown };
    try {
      body = { agentId, input: JSON.parse(inputText) };
    } catch {
      alert('Input must be valid JSON');
      setIsRunning(false);
      return;
    }

    const res = await fetch('/api-proxy/agent-tasks', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    if (!res.ok) {
      const txt = await res.text();
      alert(`Failed to create task: ${res.status} ${txt}`);
      setIsRunning(false);
      return;
    }
    const created = await res.json();
    const id = created.id as string;
    setTaskId(id);
    setRecent((r) => [id, ...r].slice(0, 5));
    // polling is handled by the hook
  }

  useEffect(() => {
    if (!polledTask) return;
    const tv: TaskView = {
      status: polledTask.status,
      startedAt: polledTask.startedAt,
      finishedAt: polledTask.finishedAt,
      errorMessage: polledTask.errorMessage ?? null,
    };
    setTask(tv);
    if (isDone) setIsRunning(false);
  }, [polledTask, isDone]);

  const statusBadge = useMemo(() => {
    if (!task) return null;
    const color =
      task.status === 'Running'
        ? 'bg-blue-100 text-blue-800'
        : task.status === 'Succeeded'
          ? 'bg-green-100 text-green-800'
          : task.status === 'Failed'
            ? 'bg-red-100 text-red-800'
            : 'bg-gray-100 text-gray-800';
    return (
      <span className={`inline-flex items-center rounded px-2 py-0.5 text-xs font-medium ${color}`}>
        {task.status}
      </span>
    );
  }, [task]);

  return (
    <div className="space-y-6">
      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (!agentId) return;
          createTask();
        }}
        className="space-y-4"
      >
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 items-start">
          <label htmlFor="agentSelect" className="text-sm font-medium text-gray-700">
            Agent
          </label>
          <select
            className="sm:col-span-2 block w-full rounded border-gray-300 bg-white p-2 text-sm shadow-sm"
            value={agentId}
            onChange={(e) => setAgentId(e.target.value)}
            title="Agent"
            id="agentSelect"
          >
            {agents.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
          </select>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 items-start">
          <label htmlFor="inputJsonTextarea" className="text-sm font-medium text-gray-700">
            Input JSON
          </label>
          <textarea
            className="sm:col-span-2 block w-full rounded border-gray-300 p-2 text-sm font-mono shadow-sm"
            rows={8}
            value={inputText}
            onChange={(e) => setInputText(e.target.value)}
            title="Input JSON"
            placeholder={`{\n  "topic": "Beatitudes"\n}`}
            id="inputJsonTextarea"
          />
        </div>

        <div className="flex items-center gap-3">
          <button
            type="submit"
            className="inline-flex items-center rounded bg-black px-3 py-1.5 text-white text-sm disabled:opacity-50"
            disabled={isRunning}
          >
            {isRunning ? 'Running…' : 'Run'}
          </button>
          {task && statusBadge}
          {error && <span className="text-sm text-red-600">{error}</span>}
          {!error && task?.errorMessage && (
            <span className="text-sm text-red-600">{task.errorMessage}</span>
          )}
        </div>
      </form>

      {taskId && (
        <div className="space-y-2">
          <h2 className="text-lg font-medium">Traces</h2>
          {/* Token summary badges */}
          <div className="flex flex-wrap gap-2 text-xs">
            {polledTask?.totalTokens != null && polledTask.totalTokens > 0 && (
              <span className="inline-flex items-center rounded bg-gray-100 px-2 py-0.5 font-medium text-gray-800">
                Total tokens: {polledTask.totalTokens}
              </span>
            )}
            {polledTask?.totalPromptTokens != null && polledTask?.totalCompletionTokens != null && (
              <span className="inline-flex items-center rounded bg-gray-100 px-2 py-0.5 font-medium text-gray-800">
                Prompt / Completion: {polledTask.totalPromptTokens} /{' '}
                {polledTask.totalCompletionTokens}
              </span>
            )}
            {polledTask?.estimatedCostUsd != null && (
              <span className="inline-flex items-center rounded bg-gray-100 px-2 py-0.5 font-medium text-gray-800">
                Est. cost: ${'{'}polledTask.estimatedCostUsd.toFixed(4){'}'}
              </span>
            )}
          </div>
          <TracesTable traces={traces} />
        </div>
      )}

      {polledTask?.status === 'Succeeded' && polledTask.result != null && (
        <div className="space-y-2">
          <h2 className="text-lg font-medium">Result</h2>
          <pre className="overflow-auto rounded bg-gray-50 p-3 text-xs">
            <code>{String(JSON.stringify(polledTask.result as object, null, 2))}</code>
          </pre>
        </div>
      )}

      <div className="space-y-2">
        <h2 className="text-lg font-medium">Recent Runs</h2>
        <ul className="list-disc pl-6 text-sm text-gray-700">
          {recent.map((id) => (
            <li key={id} className="font-mono">
              {id}
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}
