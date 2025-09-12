import { useEffect, useRef, useState } from 'react';

export type TaskDetails = {
  id: string;
  agentId: string;
  status: 'Pending' | 'Running' | 'Succeeded' | 'Failed' | 'Canceled' | string;
  createdAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  totalPromptTokens?: number;
  totalCompletionTokens?: number;
  totalTokens?: number;
  estimatedCostUsd?: number | null;
  result?: unknown;
  errorMessage?: string | null;
};

export type TraceDto = {
  id: string;
  stepNumber: number;
  kind: string;
  name: string;
  durationMs: number;
  promptTokens: number | null;
  completionTokens: number | null;
  error?: string | null;
  input: unknown;
  output?: unknown;
  createdAt: string;
};

export function useTaskPolling(taskId: string | null) {
  const [task, setTask] = useState<TaskDetails | null>(null);
  const [traces, setTraces] = useState<TraceDto[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [isDone, setIsDone] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => {
    // reset state when id changes
    setTask(null);
    setTraces([]);
    setIsDone(false);
    setError(null);

    if (!taskId) {
      if (timerRef.current) clearInterval(timerRef.current);
      timerRef.current = null;
      return;
    }

    const fetchOnce = async () => {
      setIsLoading(true);
      try {
        const res = await fetch(
          `/api-proxy/agent-tasks/${encodeURIComponent(taskId)}?includeTraces=true`,
          {
            cache: 'no-store',
          },
        );
        if (!res.ok) {
          const txt = await res.text();
          throw new Error(`${res.status}: ${txt}`);
        }
        const data = await res.json();
        const details = data.task as TaskDetails;
        const traceList = (data.traces ?? []) as TraceDto[];
        setTask(details);
        setTraces(traceList);
        const terminal =
          details.finishedAt != null ||
          details.status === 'Succeeded' ||
          details.status === 'Failed' ||
          details.status === 'Canceled';
        if (terminal) {
          setIsDone(true);
          if (timerRef.current) {
            clearInterval(timerRef.current);
            timerRef.current = null;
          }
        }
      } catch (e: unknown) {
        const msg =
          e && typeof e === 'object' && 'message' in e
            ? String((e as { message?: unknown }).message)
            : 'Failed to fetch task';
        setError(msg);
        if (timerRef.current) {
          clearInterval(timerRef.current);
          timerRef.current = null;
        }
      } finally {
        setIsLoading(false);
      }
    };

    // initial fetch + start timer
    fetchOnce();
    if (timerRef.current) clearInterval(timerRef.current);
    timerRef.current = setInterval(fetchOnce, 750);

    return () => {
      if (timerRef.current) clearInterval(timerRef.current);
      timerRef.current = null;
    };
  }, [taskId]);

  return { task, traces, isLoading, isDone, error } as const;
}
