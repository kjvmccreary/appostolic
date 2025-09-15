import { renderHook, waitFor, act } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
// no fake timers; rely on real timers to avoid MSW/fetch interference
import { useTaskPolling, TaskDetails, TraceDto } from './useTaskPolling';

const mkRunning = (over: Partial<TaskDetails> = {}): TaskDetails => ({
  id: 't-1',
  agentId: 'a-1',
  status: 'Running',
  createdAt: new Date().toISOString(),
  startedAt: new Date().toISOString(),
  finishedAt: null,
  result: undefined,
  errorMessage: null,
  ...over,
});

const mkTrace = (n: number): TraceDto => ({
  id: `tr-${n}`,
  stepNumber: n,
  kind: n % 2 === 0 ? 'Tool' : 'Model',
  name: n % 2 === 0 ? 'web.search' : 'model',
  durationMs: 1,
  promptTokens: n % 2 === 0 ? 0 : 10,
  completionTokens: n % 2 === 0 ? 0 : 2,
  createdAt: new Date().toISOString(),
  input: { n },
  output: { ok: true, n },
});

// Access the MSW server provided by test/setup.ts
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const server: import('msw/node').SetupServer = (globalThis as any).__mswServer;

describe('useTaskPolling', () => {
  // use real timers

  it('polls until terminal: Running with growing traces → Succeeded with result', async () => {
    const id = 'abc-123';
    let call = 0;
    server.use(
      http.get('*/api-proxy/agent-tasks/:id', ({ request, params }) => {
        if (params.id !== id) return HttpResponse.json({ error: 'wrong id' }, { status: 404 });
        const url = new URL(request.url);
        const includeTraces = url.searchParams.get('includeTraces');
        if (includeTraces !== 'true') {
          return HttpResponse.json({ error: 'missing includeTraces' }, { status: 400 });
        }
        call++;
        if (call < 3) {
          return HttpResponse.json({
            task: mkRunning(),
            traces: Array.from({ length: call }, (_, i) => mkTrace(i + 1)),
          });
        }
        return HttpResponse.json({
          task: mkRunning({
            status: 'Succeeded',
            finishedAt: new Date().toISOString(),
            result: { ok: true },
          }),
          traces: Array.from({ length: 3 }, (_, i) => mkTrace(i + 1)),
        });
      }),
    );

    const { result, rerender } = renderHook(({ tid }) => useTaskPolling(tid), {
      initialProps: { tid: id as string | null },
    });

    // Wait for first call to resolve (Running, 1 trace)
    await waitFor(() => {
      expect(result.current.task?.status).toBe('Running');
      expect(result.current.traces.length).toBe(1);
    });

    // 2nd call - wait ~0.9s for interval
    await act(async () => {
      await new Promise((r) => setTimeout(r, 900));
    });
    await waitFor(() => {
      expect(result.current.task?.status).toBe('Running');
      expect(result.current.traces.length).toBe(2);
    });

    // 3rd call → terminal
    await act(async () => {
      await new Promise((r) => setTimeout(r, 900));
    });
    await waitFor(() => {
      expect(result.current.isLoading).toBe(false);
      expect(result.current.isDone).toBe(true);
      expect(result.current.task?.status).toBe('Succeeded');
      expect(result.current.traces.length).toBeGreaterThanOrEqual(2);
    });

    // Change task id resets state
    await act(async () => {
      rerender({ tid: null });
    });
    expect(result.current.task).toBeNull();
    expect(result.current.traces).toEqual([]);
  });

  it('handles Failed with errorMessage', async () => {
    const id = 'fail-1';
    server.use(
      http.get('*/api-proxy/agent-tasks/:id', ({ request, params }) => {
        if (params.id !== id) return HttpResponse.json({ error: 'wrong id' }, { status: 404 });
        const url = new URL(request.url);
        if (url.searchParams.get('includeTraces') !== 'true') {
          return HttpResponse.json({ error: 'missing includeTraces' }, { status: 400 });
        }
        return HttpResponse.json({
          task: mkRunning({
            status: 'Failed',
            finishedAt: new Date().toISOString(),
            errorMessage: 'boom',
          }),
          traces: [mkTrace(1)],
        });
      }),
    );

    const { result } = renderHook(() => useTaskPolling(id));
    await waitFor(() => {
      expect(result.current.isDone).toBe(true);
      expect(result.current.task?.status).toBe('Failed');
      expect(result.current.task?.errorMessage).toBe('boom');
    });
  });
});
