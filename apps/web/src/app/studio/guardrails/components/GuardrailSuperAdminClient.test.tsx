import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import userEvent from '@testing-library/user-event';
import { render, screen, waitFor, within } from '../../../../../test/utils';
import type { GuardrailSuperadminSummary } from '../types';
import { GuardrailSuperAdminClient } from './GuardrailSuperAdminClient';

const refreshMock = vi.fn();

vi.mock('next/navigation', async () => {
  const actual = await vi.importActual<typeof import('next/navigation')>('next/navigation');
  return {
    ...actual,
    useRouter: () => ({ refresh: refreshMock }),
  };
});

const summary: GuardrailSuperadminSummary = {
  systemPolicies: [
    {
      id: 'policy-1',
      slug: 'baseline-core',
      name: 'Baseline Core',
      description: 'Primary baseline for all tenants',
      version: 2,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      definition: {
        allow: ['allow:baseline'],
        deny: ['deny:restricted'],
      },
    },
  ],
  presets: [
    {
      id: 'preset-core',
      name: 'Preset Core',
      notes: 'Default for most churches',
      version: 1,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      definition: {
        allow: ['allow:preset'],
        deny: [],
      },
    },
  ],
  activity: [
    {
      policyId: 'policy-1',
      tenantId: 'tenant-1',
      tenantName: 'Grace Temple',
      key: 'default',
      layer: 'tenantbase',
      version: 3,
      updatedByEmail: 'admin@example.com',
      action: 'published',
      occurredAt: new Date().toISOString(),
      derivedFromPresetId: 'preset-core',
      isActive: true,
      publishedAt: new Date().toISOString(),
    },
  ],
};

describe('GuardrailSuperAdminClient', () => {
  const originalFetch = global.fetch;
  const fetchMock = vi.fn();

  beforeEach(() => {
    refreshMock.mockReset();
    fetchMock.mockReset();
    global.fetch = fetchMock as unknown as typeof fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('renders system policies, presets, and activity', async () => {
    render(<GuardrailSuperAdminClient summary={summary} />);

    expect(await screen.findByText(/Guardrail Platform Console/i)).toBeInTheDocument();
    expect(screen.getByText(/baseline-core/i)).toBeInTheDocument();
    expect(screen.getByText(/Preset Core/i)).toBeInTheDocument();
    expect(screen.getByText(/Grace Temple/i)).toBeInTheDocument();
  });

  it('updates a system policy and refreshes on success', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 200 }));

    render(<GuardrailSuperAdminClient summary={summary} />);

    const card = await screen.findByTestId('system-policy-card-baseline-core');
    const definitionField = within(card).getByLabelText(/Definition/i);

    const user = userEvent.setup();

    await user.click(definitionField);
    await user.clear(definitionField);
    const updatedDefinition = {
      allow: ['allow:updated'],
      deny: [],
    };
    const updatedJson = JSON.stringify(updatedDefinition, null, 2);
    await user.paste(updatedJson);

    const saveButton = within(card).getByRole('button', { name: /save/i });
    await user.click(saveButton);

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const [url, options] = fetchMock.mock.lastCall as [RequestInfo | URL, RequestInit];
    expect(String(url)).toContain('/api-proxy/guardrails/super/system/baseline-core');
    expect(options?.method).toBe('PUT');
    expect(options?.body).toContain('allow:updated');

    await waitFor(() => expect(refreshMock).toHaveBeenCalled());
    expect(await screen.findByText(/System policy saved/i)).toBeInTheDocument();
  });

  it('shows a helpful error when definition JSON is invalid', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 200 }));

    render(<GuardrailSuperAdminClient summary={summary} />);

    const card = await screen.findByTestId('system-policy-card-baseline-core');
    const definitionField = within(card).getByLabelText(/Definition/i);

    await userEvent.clear(definitionField);
    await userEvent.type(definitionField, '{{ not-json }}');

    const saveButton = within(card).getByRole('button', { name: /save/i });
    await userEvent.click(saveButton);

    expect(await screen.findByText(/Definition must be valid JSON/i)).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
