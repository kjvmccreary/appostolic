import { afterEach, describe, expect, it, vi } from 'vitest';
import React from 'react';
import { render, screen } from '../../../../test/utils';
import type { ReactElement } from 'react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';

// vitest.setup exposes __mswServer
const server = (globalThis as { __mswServer?: import('msw/node').SetupServer }).__mswServer!;

// SUT imports
import Page from './page';

const pushMock = vi.fn();
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: pushMock }),
  usePathname: () => '/studio/tasks',
  useSearchParams: () => new URLSearchParams('take=20&skip=0'),
}));

// The page renders MUI DateTimePicker which requires a LocalizationProvider context.
// In unit tests we don't exercise date inputs, so mock it to a simple input to avoid context issues.
vi.mock('@mui/x-date-pickers/DateTimePicker', () => ({
  DateTimePicker: (props: { label?: string }) =>
    React.createElement('input', { 'aria-label': props.label ?? 'DateTimePicker' }),
}));

function mockPageData({ total = 3 }: { total?: number } = {}) {
  const items = [
    {
      id: 't1',
      agentId: 'a1',
      status: 'Running',
      createdAt: new Date().toISOString(),
      totalTokens: 10,
    },
    {
      id: 't2',
      agentId: 'a2',
      status: 'Succeeded',
      createdAt: new Date().toISOString(),
      totalTokens: 20,
    },
  ];
  return { items, total };
}

const agents = [
  { id: 'a1', name: 'Researcher' },
  { id: 'a2', name: 'Writer' },
];

describe('Inbox page (/studio/tasks)', () => {
  beforeEach(() => {
    // GET /api-proxy/agent-tasks
    server.use(
      http.get('http://localhost/api-proxy/agent-tasks', ({ request }) => {
        const url = new URL(request.url);
        // Basic parse ensures handler works; assertions done via router.push expectations
        url.searchParams.get('status');
        url.searchParams.get('from');
        url.searchParams.get('to');
        url.searchParams.get('q');
        url.searchParams.get('take');
        url.searchParams.get('skip');
        const data = mockPageData({ total: 27 });
        return HttpResponse.json(data.items, {
          headers: { 'x-total-count': String(data.total) },
        });
      }),
      // GET /api-proxy/agents
      http.get('http://localhost/api-proxy/agents', () => HttpResponse.json(agents)),
    );
  });
  afterEach(() => server.resetHandlers());

  it('renders grid and applies filter via router.push', async () => {
    type PageFn = (args: {
      searchParams: Record<string, string | string[] | undefined>;
    }) => Promise<ReactElement>;
    const ui = await (Page as unknown as PageFn)({ searchParams: { take: '20', skip: '0' } });
    render(ui);

    const grid = await screen.findByRole('grid');
    expect(grid).toBeInTheDocument();

    // Click the Running status chip
    const runningChip = await screen.findByRole('button', { name: /running/i });
    await userEvent.click(runningChip);
    expect(pushMock).toHaveBeenCalled();
    const url = String(pushMock.mock.calls.at(-1)?.[0]);
    expect(url).toContain('/studio/tasks?');
    expect(url).toMatch(/status=Running/);
    expect(url).toMatch(/take=20/);
    expect(url).toMatch(/skip=0/);
  });

  it('pagination next updates skip/take in router.push', async () => {
    type PageFn = (args: {
      searchParams: Record<string, string | string[] | undefined>;
    }) => Promise<ReactElement>;
    const ui = await (Page as unknown as PageFn)({ searchParams: { take: '20', skip: '0' } });
    render(ui);
    const nextBtn = await screen.findByLabelText(/go to next page/i);
    await userEvent.click(nextBtn);
    const url = String(pushMock.mock.calls.at(-1)?.[0]);
    expect(url).toMatch(/take=20/);
    expect(url).toMatch(/skip=20/);
  });

  it('copies ID from table row', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText } as unknown as Clipboard,
    });
    type PageFn = (args: {
      searchParams: Record<string, string | string[] | undefined>;
    }) => Promise<React.ReactElement>;
    const ui = await (Page as unknown as PageFn)({ searchParams: { take: '20', skip: '0' } });
    render(ui);
    // Button label includes the id
    const btn = await screen.findByRole('button', { name: /copy task id t1/i });
    await userEvent.click(btn);
    expect(writeText).toHaveBeenCalledWith('t1');
  });
});
