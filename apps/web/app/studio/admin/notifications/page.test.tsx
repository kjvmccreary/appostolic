import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '../../../../test/utils';
import type { ReactElement } from 'react';

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

type DlqItem = {
  id: string;
  kind: string;
  toEmail: string;
  status: string;
  attemptCount: number;
  createdAt: string;
  lastError?: string | null;
};

describe('notifications DLQ page (server)', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('renders 403 message for non-admin membership', async () => {
    vi.mocked(getServerSession).mockResolvedValue({
      user: { email: 'u@example.com' },
      tenant: 't1',
      memberships: [{ tenantId: 'tid', tenantSlug: 't1', role: 'Viewer' }],
    } as unknown as Parameters<typeof getServerSession>[0]);

    const Comp = Page as unknown as (args?: {
      searchParams?: Record<string, string>;
    }) => Promise<ReactElement>;
    const ui = await Comp();
    render(ui);
    expect(await screen.findByText(/403/i)).toBeInTheDocument();
  });

  it('shows empty state when no items and computes pager text', async () => {
    vi.mocked(getServerSession).mockResolvedValue({
      user: { email: 'admin@example.com' },
      tenant: 't1',
      memberships: [{ tenantId: 'tid', tenantSlug: 't1', role: 'Admin' }],
    } as unknown as Parameters<typeof getServerSession>[0]);

    vi.mocked(fetchFromProxy).mockResolvedValue({
      ok: true,
      headers: new Headers({ 'X-Total-Count': '0' }),
      json: async () => [] as DlqItem[],
    } as unknown as Response);

    const Comp = Page as unknown as (args?: {
      searchParams?: Record<string, string>;
    }) => Promise<ReactElement>;
    const ui = await Comp();
    render(ui);
    expect(await screen.findByText(/No items found/i)).toBeInTheDocument();
    expect(await screen.findByText(/Page 1 of 1 — Total 0/i)).toBeInTheDocument();
  });

  it('Prev/Next links preserve filters and compute skip', async () => {
    vi.mocked(getServerSession).mockResolvedValue({
      user: { email: 'admin@example.com' },
      tenant: 't1',
      memberships: [{ tenantId: 'tid', tenantSlug: 't1', role: 'Admin' }],
    } as unknown as Parameters<typeof getServerSession>[0]);

    const rows: DlqItem[] = new Array(25).fill(0).map((_, i) => ({
      id: String(i + 1),
      kind: 'Verification',
      toEmail: `u${i + 1}@example.com`,
      status: 'DeadLetter',
      attemptCount: 3,
      createdAt: new Date().toISOString(),
      lastError: 'SMTP error',
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
      searchParams: { take: '25', skip: '25', status: 'DeadLetter', kind: 'Verification' },
    });
    render(ui);
    // Prev should link to skip=0 and include filters
    const prev = await screen.findByRole('link', { name: /prev/i });
    expect(prev).toHaveAttribute('href', expect.stringContaining('take=25'));
    expect(prev).toHaveAttribute('href', expect.stringContaining('skip=0'));
    expect(prev).toHaveAttribute('href', expect.stringContaining('status=DeadLetter'));
    expect(prev).toHaveAttribute('href', expect.stringContaining('kind=Verification'));
    // Next should link to skip=50
    const next = await screen.findByRole('link', { name: /next/i });
    expect(next).toHaveAttribute('href', expect.stringContaining('skip=50'));
  });
});
