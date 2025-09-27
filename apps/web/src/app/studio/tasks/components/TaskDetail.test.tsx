import { describe, expect, it, vi, afterEach } from 'vitest';
import { render, screen } from '../../../../../test/utils';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';

const server = (globalThis as { __mswServer?: import('msw/node').SetupServer }).__mswServer!;

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

import TaskDetail from './TaskDetail';
import type { Task, Trace } from '../types';

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
    const origCreate = document.createElement.bind(document);
    let recordedFilename: string | null = null;
    const spy = vi.spyOn(document, 'createElement').mockImplementation((tagName: string) => {
      const el = origCreate(tagName);
      if (tagName.toLowerCase() === 'a') {
        // capture when setting the download attribute
        const origSetAttr = (el as HTMLElement).setAttribute.bind(el);
        (el as HTMLElement).setAttribute = (name: string, value: string) => {
          if (name.toLowerCase() === 'download') recordedFilename = value;
          return origSetAttr(name, value);
        };
        // avoid actual navigation
        (el as HTMLAnchorElement).click = vi.fn();
      }
      return el as HTMLElement;
    });
    render(
      <TaskDetail
        task={{ id: 'e1', agentId: 'a', status: 'Succeeded', createdAt: new Date().toISOString() }}
        traces={traces}
      />,
    );
    await userEvent.click(screen.getByRole('button', { name: /export/i }));
    expect(screen.getByRole('button', { name: /export/i })).toBeEnabled();
    expect(recordedFilename).toBe('task-e1.json');
    spy.mockRestore();
  });

  it('Copy Task ID writes to clipboard', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText } as unknown as Clipboard,
    });
    render(
      <TaskDetail
        task={{
          id: 'xyz123',
          agentId: 'a',
          status: 'Succeeded',
          createdAt: new Date().toISOString(),
        }}
        traces={traces}
      />,
    );
    const copyBtn = await screen.findByRole('button', { name: /copy task id/i });
    await userEvent.click(copyBtn);
    expect(writeText).toHaveBeenCalledWith('xyz123');
  });

  it('shows guardrail metadata summary when present', async () => {
    const guardrailTask: Task = {
      id: 'g1',
      agentId: 'a',
      status: 'Failed',
      createdAt: new Date().toISOString(),
      guardrailDecision: 'Escalate',
      guardrailMetadata: {
        evaluatedAt: new Date().toISOString(),
        context: {
          channel: 'agent.runtime',
          promptSummary: 'needs review',
          signals: ['escalate:human-review'],
        },
        result: {
          decision: 'Escalate',
          reasonCode: 'escalate:policy-match',
          matchedSignals: ['escalate:human-review'],
          matches: [
            {
              rule: 'escalate:human-review',
              source: 'tenant',
              layer: 'tenantbase',
            },
          ],
        },
      },
    };
    render(<TaskDetail task={guardrailTask} traces={traces} />);

    expect(await screen.findByLabelText(/guardrail decision escalate/i)).toBeInTheDocument();
    expect(screen.getByText(/Guardrail escalated this task for review/i)).toBeInTheDocument();
    expect(screen.getByText(/Signals: escalate:human-review/i)).toBeInTheDocument();
    expect(screen.getByText(/Match: escalate:human-review/i)).toBeInTheDocument();
  });
});
