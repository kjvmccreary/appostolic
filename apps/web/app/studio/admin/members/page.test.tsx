import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '../../../../test/utils';
import type { ReactElement } from 'react';

// Mock next-auth server session
vi.mock('next-auth', () => ({ getServerSession: vi.fn() }));
vi.mock('../../../../src/lib/auth', () => ({ authOptions: {} }));

// Mock cookies()
vi.mock('next/headers', () => ({
  cookies: () => ({ get: (name: string) => (name === 'selected_tenant' ? { value: 't1' } : null) }),
}));

// Mock server fetch helper to return memberships JSON
vi.mock('../../../../app/lib/serverFetch', () => ({
  fetchFromProxy: vi.fn(),
}));

import Page from './page';
import { getServerSession } from 'next-auth';
import { fetchFromProxy } from '../../../../app/lib/serverFetch';

describe('/studio/admin/members page', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });
  afterEach(() => {
    vi.clearAllMocks();
  });

  it('renders 403 message for non-admin membership', async () => {
    vi.mocked(getServerSession).mockResolvedValue({
      user: { email: 'u@example.com' },
      memberships: [
        { tenantId: 't-1', tenantSlug: 't1', role: 'Viewer' },
        { tenantId: 't-2', tenantSlug: 't2', role: 'Admin' },
      ],
    } as unknown as Parameters<typeof getServerSession>[0]);

    const ui = (await (Page as unknown as () => Promise<ReactElement>)()) as ReactElement;
    render(ui);
    expect(await screen.findByText(/403 — Access denied/i)).toBeInTheDocument();
  });

  it('disables unchecking last admin for sole Admin member', async () => {
    vi.mocked(getServerSession).mockResolvedValue({
      user: { email: 'admin@example.com' },
      tenant: 't1',
      memberships: [{ tenantId: 't-1', tenantSlug: 't1', role: 'Admin' }],
    } as unknown as Parameters<typeof getServerSession>[0]);

    const members = [
      {
        userId: 'u1',
        email: 'admin@example.com',
        role: 'Admin',
        roles: 'TenantAdmin,Creator',
        rolesValue: 5,
        joinedAt: new Date().toISOString(),
      },
      {
        userId: 'u2',
        email: 'creator@example.com',
        role: 'Editor',
        roles: 'Creator',
        rolesValue: 4,
        joinedAt: new Date().toISOString(),
      },
    ];
    vi.mocked(fetchFromProxy).mockResolvedValue(
      new Response(JSON.stringify(members), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }) as unknown as Response,
    );

    const ui = (await (Page as unknown as () => Promise<ReactElement>)()) as ReactElement;
    render(ui);

    // There are two Admin checkboxes; the first belongs to the only TenantAdmin and should be disabled.
    const adminCheckboxes = await screen.findAllByRole('checkbox', { name: /admin/i });
    expect(adminCheckboxes).toHaveLength(2);
    expect(adminCheckboxes[0]).toBeDisabled();
    expect(adminCheckboxes[1]).not.toBeDisabled();
  });
});
