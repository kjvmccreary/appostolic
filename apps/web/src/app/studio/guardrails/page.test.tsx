import { describe, expect, it, beforeEach, afterEach, vi } from 'vitest';
import userEvent from '@testing-library/user-event';
import { fireEvent, render, screen } from '../../../../test/utils';
import { http, HttpResponse } from 'msw';
import type { ReactElement } from 'react';
import type { TenantGuardrailSummary } from './types';

const server = (globalThis as { __mswServer?: import('msw/node').SetupServer }).__mswServer!;

// Capture fetch bodies for assertions
let lastDraftBody: unknown = null;
let lastResetBody: unknown = null;
let publishCount = 0;

const refreshMock = vi.fn();

vi.mock('next/navigation', async () => {
  const actual = await vi.importActual<typeof import('next/navigation')>('next/navigation');
  return {
    ...actual,
    useRouter: () => ({ refresh: refreshMock }),
  };
});

import Page from './page';

const summary: TenantGuardrailSummary = {
  key: 'default',
  policies: [
    {
      id: 'base-policy',
      key: 'default',
      layer: 'tenantbase',
      version: 2,
      isActive: true,
      derivedFromPresetId: 'preset-core',
      createdByUserId: 'user-a',
      updatedByUserId: 'user-a',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      publishedAt: new Date().toISOString(),
      metadata: null,
      definition: {
        allow: ['allow:active'],
        deny: [],
        escalate: [],
      },
    },
    {
      id: 'draft-policy',
      key: 'default',
      layer: 'draft',
      version: 1,
      isActive: false,
      derivedFromPresetId: 'preset-core',
      createdByUserId: 'user-b',
      updatedByUserId: 'user-b',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      publishedAt: null,
      metadata: null,
      definition: {
        allow: ['allow:active', 'allow:draft-only'],
        deny: [],
        escalate: ['escalate:review'],
      },
    },
  ],
  snapshot: {
    decision: 'Allow',
    reasonCode: 'allow:active',
    matchedSignals: [],
    policy: {
      allow: ['allow:active'],
      deny: [],
      escalate: [],
    },
    matches: [],
    trace: [],
  },
  presets: [
    { id: 'preset-core', name: 'Core Baseline', notes: 'Default preset', version: 1 },
    { id: 'preset-youth', name: 'Youth Ministry', notes: 'Relaxed escalate rules', version: 1 },
  ],
};

describe('Guardrails admin page', () => {
  beforeEach(() => {
    lastDraftBody = null;
    lastResetBody = null;
    publishCount = 0;
    refreshMock.mockReset();

    server.use(
      http.get('http://localhost/api-proxy/guardrails/tenant', () => HttpResponse.json(summary)),
      http.put(
        'http://localhost/api-proxy/guardrails/tenant/default/draft',
        async ({ request }) => {
          lastDraftBody = await request.json();
          return HttpResponse.json({
            ...summary.policies[1],
            definition: (lastDraftBody as { definition: unknown }).definition,
            derivedFromPresetId:
              (lastDraftBody as { derivedFromPresetId?: string | null }).derivedFromPresetId ??
              null,
          });
        },
      ),
      http.post('http://localhost/api-proxy/guardrails/tenant/default/publish', async () => {
        publishCount += 1;
        return HttpResponse.json(summary.policies[0]);
      }),
      http.post(
        'http://localhost/api-proxy/guardrails/tenant/default/reset',
        async ({ request }) => {
          lastResetBody = await request.json();
          return HttpResponse.json(summary.policies[0]);
        },
      ),
    );
  });

  afterEach(() => server.resetHandlers());

  it('renders snapshot and policy diff', async () => {
    const ui = await (Page as unknown as () => Promise<ReactElement>)();
    render(ui);

    expect(await screen.findByText(/Tenant Guardrails/i)).toBeInTheDocument();
    expect(screen.getByText(/Decision: Allow/i)).toBeInTheDocument();
    expect(screen.getByText(/Draft vs Active Policy/i)).toBeInTheDocument();
    expect(screen.getAllByText(/allow:draft-only/i)).not.toHaveLength(0);
  });

  it('saves draft edits and triggers refresh', async () => {
    const ui = await (Page as unknown as () => Promise<ReactElement>)();
    render(ui);

    const textarea = await screen.findByLabelText(/Draft Definition/i);
    await userEvent.clear(textarea);
    const updated = {
      allow: ['allow:new'],
      deny: ['deny:rule'],
      escalate: [],
    };
    fireEvent.change(textarea, { target: { value: JSON.stringify(updated, null, 2) } });
    const saveBtn = await screen.findByRole('button', { name: /save draft/i });
    await userEvent.click(saveBtn);

    expect(await screen.findByText(/Draft saved/i)).toBeInTheDocument();
    expect(lastDraftBody).toMatchObject({
      definition: updated,
    });
    expect(refreshMock).toHaveBeenCalled();
  });

  it('publishes current draft', async () => {
    const ui = await (Page as unknown as () => Promise<ReactElement>)();
    render(ui);

    const publishBtn = await screen.findByRole('button', { name: /publish/i });
    await userEvent.click(publishBtn);

    expect(await screen.findByText(/Guardrail published/i)).toBeInTheDocument();
    expect(publishCount).toBe(1);
    expect(refreshMock).toHaveBeenCalled();
  });

  it('resets to selected preset', async () => {
    const ui = await (Page as unknown as () => Promise<ReactElement>)();
    render(ui);

    const selectInput = (await screen.findByLabelText(/Derived preset/i)) as HTMLInputElement;
    const trigger = selectInput.parentElement?.querySelector(
      '[role="combobox"],[role="button"]',
    ) as HTMLElement | null;
    await userEvent.click(trigger ?? selectInput);
    const option = await screen.findByText('Youth Ministry');
    await userEvent.click(option);

    const resetBtn = await screen.findByRole('button', { name: /reset to preset/i });
    await userEvent.click(resetBtn);

    expect(await screen.findByText(/Guardrail reset to preset/i)).toBeInTheDocument();
    expect(lastResetBody).toMatchObject({ presetId: 'preset-youth' });
    expect(refreshMock).toHaveBeenCalled();
  });
});
