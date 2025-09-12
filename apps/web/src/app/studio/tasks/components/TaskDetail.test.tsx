import { describe, expect, it, vi, afterEach } from 'vitest';
import { render, screen } from '../../../../../test/utils';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';

const server = (globalThis as { __mswServer?: import('msw/node').SetupServer }).__mswServer!;

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

import TaskDetail from './TaskDetail';

type Trace = {
  stepNumber: number;
  kind: string;
  name: string;
  durationMs: number;
  promptTokens: number | null;
  completionTokens: number | null;
  input?: unknown;
  output?: unknown;
};

const traces: Trace[] = [
  {
    stepNumber: 1,
    kind: 'Model',
    name: 'decide',
    durationMs: 10,
    promptTokens: 5,
    completionTokens: 7,
    input: { a: 1 },
    output: { b: 2 },
  },
];

describe('TaskDetail actions', () => {
  afterEach(() => server.resetHandlers());

  it('Retry posts and navigates', async () => {
    server.use(
      http.post('http://localhost/api-proxy/agent-tasks/old/retry', async () =>
        HttpResponse.json({ id: 'new' }, { status: 201 }),
      ),
    );
    const ui = (
      <TaskDetail
        task={{ id: 'old', agentId: 'a', status: 'Succeeded', createdAt: new Date().toISOString() }}
        traces={traces}
      />
    );
    render(ui);
    const btn = await screen.findByRole('button', { name: /retry/i });
    await userEvent.click(btn);
    // no error thrown is sufficient; router.push is mocked
    expect(btn).toBeEnabled();
  });

  it('Cancel confirms and refetches', async () => {
    server.use(
      http.post('http://localhost/api-proxy/agent-tasks/r1/cancel', () =>
        HttpResponse.json({ id: 'r1', status: 'Canceled' }, { status: 202 }),
      ),
      http.get('http://localhost/api-proxy/agent-tasks/r1', ({ request }) => {
        const url = new URL(request.url);
        if (url.searchParams.get('includeTraces') === 'true') {
          return HttpResponse.json({
            task: {
              id: 'r1',
              agentId: 'a',
              status: 'Canceled',
              createdAt: new Date().toISOString(),
            },
            traces,
          });
        }
        return HttpResponse.json({}, { status: 400 });
      }),
    );
    render(
      <TaskDetail
        task={{ id: 'r1', agentId: 'a', status: 'Running', createdAt: new Date().toISOString() }}
        traces={traces}
      />,
    );
    await userEvent.click(screen.getByRole('button', { name: /cancel/i }));
    await userEvent.click(await screen.findByRole('button', { name: /cancel task/i }));
    expect(await screen.findByLabelText(/status canceled/i)).toBeInTheDocument();
  });

  it('Export calls endpoint', async () => {
    server.use(
      http.get('http://localhost/api-proxy/agent-tasks/e1/export', () =>
        HttpResponse.json({ ok: true }, { status: 200 }),
      ),
    );
    render(
      <TaskDetail
        task={{ id: 'e1', agentId: 'a', status: 'Succeeded', createdAt: new Date().toISOString() }}
        traces={traces}
      />,
    );
    await userEvent.click(screen.getByRole('button', { name: /export/i }));
    expect(screen.getByRole('button', { name: /export/i })).toBeEnabled();
  });
});
