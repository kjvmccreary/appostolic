import React from 'react';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import AgentRunForm from '../../../../../app/dev/agents/components/AgentRunForm';

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const server: import('msw/node').SetupServer = (globalThis as any).__mswServer;

describe('AgentRunForm', () => {
  it('submits, polls, shows traces, badges, and final result', async () => {
    const agentId = 'agent-1';
    const agents = [{ id: agentId, name: 'Research Agent' }];

    const createdId = 'task-xyz';
    const createBodies: unknown[] = [];
    let getCount = 0;

    // POST create handler
    server.use(
      http.post('*/api-proxy/agent-tasks', async ({ request }) => {
        const body = await request.json();
        createBodies.push(body);
        return HttpResponse.json({ id: createdId, status: 'Pending' }, { status: 201 });
      }),
    );

    // GET polling handler
    server.use(
      http.get('*/api-proxy/agent-tasks/:id', ({ request, params }) => {
        if (params.id !== createdId)
          return HttpResponse.json({ error: 'wrong id' }, { status: 404 });
        const url = new URL(request.url);
        if (url.searchParams.get('includeTraces') !== 'true') {
          return HttpResponse.json({ error: 'missing includeTraces' }, { status: 400 });
        }
        getCount++;
        if (getCount === 1) {
          return HttpResponse.json({
            task: {
              id: createdId,
              agentId,
              status: 'Running',
              createdAt: new Date().toISOString(),
              startedAt: new Date().toISOString(),
              finishedAt: null,
              errorMessage: null,
              totalPromptTokens: 10,
              totalCompletionTokens: 2,
              totalTokens: 12,
              estimatedCostUsd: 0.0012,
            },
            traces: [
              {
                id: 'tr-1',
                stepNumber: 1,
                kind: 'Model',
                name: 'model',
                durationMs: 1,
                promptTokens: 10,
                completionTokens: 2,
                error: null,
                input: { prompt: '...' },
                output: { plan: 'use web.search' },
                createdAt: new Date().toISOString(),
              },
            ],
          });
        }
        return HttpResponse.json({
          task: {
            id: createdId,
            agentId,
            status: 'Succeeded',
            createdAt: new Date().toISOString(),
            startedAt: new Date().toISOString(),
            finishedAt: new Date().toISOString(),
            errorMessage: null,
            result: { ok: true, answer: 'Done' },
            totalPromptTokens: 12,
            totalCompletionTokens: 3,
            totalTokens: 15,
            estimatedCostUsd: 0.0015,
          },
          traces: [
            {
              id: 'tr-1',
              stepNumber: 1,
              kind: 'Model',
              name: 'model',
              durationMs: 1,
              promptTokens: 10,
              completionTokens: 2,
              error: null,
              input: { prompt: '...' },
              output: { plan: 'use web.search' },
              createdAt: new Date().toISOString(),
            },
            {
              id: 'tr-2',
              stepNumber: 2,
              kind: 'Tool',
              name: 'web.search',
              durationMs: 1,
              promptTokens: 0,
              completionTokens: 0,
              error: null,
              input: { q: 'intro' },
              output: { hits: [{ title: 'Intro' }] },
              createdAt: new Date().toISOString(),
            },
          ],
        });
      }),
    );

    render(<AgentRunForm agents={agents} />);

    // Ensure defaults are present
    await screen.findByLabelText('Agent');
    await screen.findByLabelText('Input JSON');
    const runBtn = await screen.findByRole('button', { name: /run/i });

    // Submit
    await userEvent.click(runBtn);

    // POST fired with agentId and parsed input
    await screen.findByText('Traces'); // section appears once taskId set
    expect(createBodies.length).toBe(1);
    const postBody = createBodies[0] as { agentId: string; input: unknown };
    expect(postBody.agentId).toBe(agentId);
    expect(postBody.input).toMatchObject({ topic: expect.any(String) });

    // Status transitions to Running
    const runningBadge = await screen.findByText('Running');
    expect(runningBadge).toBeInTheDocument();

    // Table shows at least one trace row
    const table = await screen.findByRole('table');
    const rows = within(table).getAllByRole('row');
    expect(rows.length).toBeGreaterThan(1); // header + 1

    // Badges show totals/cost
    await screen.findByText(/Total tokens: 12|15/);
    await screen.findByText(/Prompt \/ Completion: 10 \/ 2|12 \/ 3/);
    await screen.findByText(/Est\. cost: \$0\.001[25]/);

    // Eventually Succeeded and result visible
    const succeeded = await screen.findByText('Succeeded', undefined, { timeout: 5000 });
    expect(succeeded).toBeInTheDocument();
    const resultHeading = await screen.findByRole(
      'heading',
      { name: /result/i },
      { timeout: 5000 },
    );
    expect(resultHeading).toBeInTheDocument();
    const resultCode = await screen.findByText(/"ok": true/, undefined, { timeout: 5000 });
    expect(resultCode).toBeInTheDocument();
  });
});
