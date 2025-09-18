import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '../../../../test/utils';
import type { ReactElement } from 'react';
import { mapAuditRows, AuditRow } from './mapAuditRows';

// Server-side mocks used by page component
vi.mock('next-auth', () => ({ getServerSession: vi.fn() }));
vi.mock('../../../src/lib/auth', () => ({ authOptions: {} }));
vi.mock('next/headers', () => ({
  cookies: () => ({ get: (name: string) => (name === 'selected_tenant' ? { value: 't1' } : null) }),
}));
vi.mock('../../../lib/serverFetch', () => ({ fetchFromProxy: vi.fn() }));

import Page from './page';
import { getServerSession } from 'next-auth';
import { fetchFromProxy } from '../../../lib/serverFetch';

// Focused unit test to ensure numeric role flag decoding for audits UI
// stays consistent with server [Flags] enum ordering (1,2,4,8).

describe('audits page mapping', () => {
  it('decodes single and combined role flags to names', () => {
    const rows: AuditRow[] = [
      {
        id: '1',
        userId: 'u1',
        changedByUserId: 'a1',
        changedByEmail: 'actor@example.com',
        oldRoles: 0,
        newRoles: 1, // TenantAdmin
        changedAt: new Date().toISOString(),
      },
      {
        id: '2',
        userId: 'u2',
        changedByUserId: 'a1',
        changedByEmail: 'actor@example.com',
        oldRoles: 1 | 2 | 4 | 8, // all roles
        newRoles: 2 | 4, // Approver + Creator
        changedAt: new Date().toISOString(),
      },
    ];

    const mapped = mapAuditRows(rows);
    const first = mapped[0];
    const second = mapped[1];

    expect(first.oldNames).toBe('None');
    expect(first.newNames).toContain('TenantAdmin');
    expect(second.oldNames.split(', ').length).toBeGreaterThanOrEqual(4);
    expect(second.newNames).toBe('Approver, Creator');
  });
});

describe('audits page (server)', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('renders 403 message for non-admin membership', async () => {
    vi.mocked(getServerSession).mockResolvedValue({
      user: { email: 'u@example.com' },
      tenant: 't1',
      memberships: [{ tenantId: 'tid', tenantSlug: 't1', role: 'Viewer', roles: [] }],
    } as unknown as Parameters<typeof getServerSession>[0]);

    const Comp = Page as unknown as (args?: {
      searchParams?: Record<string, string>;
    }) => Promise<ReactElement>;
    const ui = await Comp();
    render(ui);
    expect(await screen.findByText(/403 — Access denied/i)).toBeInTheDocument();
  });

  it('computes pages from X-Total-Count and renders pager text', async () => {
    vi.mocked(getServerSession).mockResolvedValue({
      user: { email: 'admin@example.com' },
      tenant: 't1',
      memberships: [{ tenantId: 'tid', tenantSlug: 't1', role: 'Viewer', roles: ['TenantAdmin'] }],
    } as unknown as Parameters<typeof getServerSession>[0]);

    const rows: AuditRow[] = [
      {
        id: '1',
        userId: 'u1',
        changedByUserId: 'a1',
        changedByEmail: 'actor@example.com',
        oldRoles: 1,
        newRoles: 2,
        changedAt: new Date().toISOString(),
      },
    ];
    vi.mocked(fetchFromProxy).mockResolvedValue({
      ok: true,
      headers: new Headers({ 'X-Total-Count': '120' }),
      json: async () => rows,
    } as unknown as Response);

    const Comp = Page as unknown as (args?: {
      searchParams?: Record<string, string>;
    }) => Promise<ReactElement>;
    const ui = await Comp();
    render(ui);
    expect(await screen.findByText(/Page 1 of 3 — Total 120/i)).toBeInTheDocument();
  });

  it('Prev/Next links preserve filters and compute skip', async () => {
    vi.mocked(getServerSession).mockResolvedValue({
      user: { email: 'admin@example.com' },
      tenant: 't1',
      memberships: [{ tenantId: 'tid', tenantSlug: 't1', role: 'Viewer', roles: ['TenantAdmin'] }],
    } as unknown as Parameters<typeof getServerSession>[0]);

    const rows: AuditRow[] = new Array(25).fill(0).map((_, i) => ({
      id: String(i),
      userId: 'u' + i,
      changedByUserId: 'a1',
      changedByEmail: 'a@example.com',
      oldRoles: 1,
      newRoles: 2,
      changedAt: new Date().toISOString(),
    }));
    vi.mocked(fetchFromProxy).mockResolvedValue({
      ok: true,
      headers: new Headers({ 'X-Total-Count': '75' }), // 3 pages at take=25
      json: async () => rows,
    } as unknown as Response);

    const Comp = Page as unknown as (args?: {
      searchParams?: Record<string, string>;
    }) => Promise<ReactElement>;
    const ui = await Comp({
      searchParams: {
        take: '25',
        skip: '25',
        userId: 'u1',
        changedByUserId: 'a1',
        from: '2025-01-01',
        to: '2025-02-01',
      },
    });
    render(ui);
    // Prev should link to skip=0 and include filters
    const prev = await screen.findByRole('link', { name: /prev/i });
    expect(prev).toHaveAttribute('href', expect.stringContaining('take=25'));
    expect(prev).toHaveAttribute('href', expect.stringContaining('skip=0'));
    expect(prev).toHaveAttribute('href', expect.stringContaining('userId=u1'));
    expect(prev).toHaveAttribute('href', expect.stringContaining('changedByUserId=a1'));
    expect(prev).toHaveAttribute('href', expect.stringContaining('from=2025-01-01'));
    expect(prev).toHaveAttribute('href', expect.stringContaining('to=2025-02-01'));
    // Next should link to skip=50
    const next = await screen.findByRole('link', { name: /next/i });
    expect(next).toHaveAttribute('href', expect.stringContaining('skip=50'));
  });
});
