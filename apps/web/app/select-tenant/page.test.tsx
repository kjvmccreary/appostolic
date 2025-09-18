import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('next-auth', () => ({ getServerSession: vi.fn() }));
vi.mock('../../src/lib/auth', () => ({ authOptions: {} }));

// Mock cookies and redirect utilities
type RedirectError = Error & { destination?: string };
const setCookieMock = vi.fn();

vi.mock('next/headers', () => ({
  cookies: () => ({ get: vi.fn(), set: setCookieMock }),
}));

vi.mock('next/navigation', async (orig) => {
  const actual = (await (orig as () => Promise<Record<string, unknown>>)()) as Record<
    string,
    unknown
  >;
  return {
    ...actual,
    redirect: (url: string) => {
      const e = new Error('REDIRECT') as RedirectError;
      e.destination = url;
      throw e;
    },
  } as Record<string, unknown>;
});

import SelectTenantPage from './page';
import { getServerSession } from 'next-auth';

describe('/select-tenant page', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    setCookieMock.mockReset();
  });

  it('redirects unauthenticated users to /login', async () => {
    vi.mocked(getServerSession).mockResolvedValue(null);
    let dest = '';
    try {
      await (SelectTenantPage as unknown as (p: unknown) => Promise<unknown>)({ searchParams: {} });
    } catch (e) {
      const err = e as RedirectError;
      if (err.message === 'REDIRECT') dest = err.destination ?? '';
      else throw e;
    }
    expect(dest).toBe('/login');
  });

  it('auto-selects when only one membership and redirects via API route to set cookie then continue', async () => {
    vi.mocked(getServerSession).mockResolvedValue({
      user: { email: 'u@example.com' },
      memberships: [{ tenantId: 't1', tenantSlug: 't1-personal', role: 'Admin' }],
    } as unknown as Parameters<typeof getServerSession>[0]);

    let dest = '';
    try {
      await (SelectTenantPage as unknown as (p: unknown) => Promise<unknown>)({ searchParams: {} });
    } catch (e) {
      const err = e as RedirectError;
      if (err.message === 'REDIRECT') dest = err.destination ?? '';
      else throw e;
    }
    expect(dest).toBe(
      `/api/tenant/select?tenant=t1-personal&next=${encodeURIComponent('/studio/agents')}`,
    );
  });

  it('respects a safe next path when provided (via API route)', async () => {
    vi.mocked(getServerSession).mockResolvedValue({
      user: { email: 'u@example.com' },
      memberships: [{ tenantId: 't1', tenantSlug: 't1-personal', role: 'Admin' }],
    } as unknown as Parameters<typeof getServerSession>[0]);

    let dest = '';
    try {
      await (SelectTenantPage as unknown as (p: unknown) => Promise<unknown>)({
        searchParams: { next: '/studio/tasks?status=open' },
      });
    } catch (e) {
      const err = e as RedirectError;
      if (err.message === 'REDIRECT') dest = err.destination ?? '';
      else throw e;
    }
    expect(dest).toBe(
      `/api/tenant/select?tenant=t1-personal&next=${encodeURIComponent('/studio/tasks?status=open')}`,
    );
  });

  it('defaults next when provided value is unsafe (external) via API route', async () => {
    vi.mocked(getServerSession).mockResolvedValue({
      user: { email: 'u@example.com' },
      memberships: [{ tenantId: 't1', tenantSlug: 't1-personal', role: 'Admin' }],
    } as unknown as Parameters<typeof getServerSession>[0]);

    let dest = '';
    try {
      await (SelectTenantPage as unknown as (p: unknown) => Promise<unknown>)({
        searchParams: { next: 'https://evil.example.com' },
      });
    } catch (e) {
      const err = e as RedirectError;
      if (err.message === 'REDIRECT') dest = err.destination ?? '';
      else throw e;
    }
    expect(dest).toBe(
      `/api/tenant/select?tenant=t1-personal&next=${encodeURIComponent('/studio/agents')}`,
    );
  });

  it('renders canonical role labels instead of legacy names', async () => {
    // Multi-tenant, ensure server component returns options labeled with canonical roles
    vi.mocked(getServerSession).mockResolvedValue({
      user: { email: 'u@example.com' },
      memberships: [
        { tenantId: 't1', tenantSlug: 'acme', role: 'Viewer' }, // -> Learner
        { tenantId: 't2', tenantSlug: 'beta', role: 'Editor' }, // -> Creator
        { tenantId: 't3', tenantSlug: 'gamma', role: 'Owner' }, // -> Admin
      ],
    } as unknown as Parameters<typeof getServerSession>[0]);

    // Render the server component by invoking it; it returns JSX we can snapshot as string
    const node = (await (SelectTenantPage as unknown as (p: unknown) => Promise<unknown>)({
      searchParams: {},
    })) as unknown as { props?: { children?: unknown } };
    const serialized = JSON.stringify(node);
    // Ensure canonical labels present (Learner/Creator/Admin) and legacy names not present
    expect(serialized).toContain('Learner');
    expect(serialized).toContain('Creator');
    expect(serialized).toContain('Admin');
    expect(serialized).not.toContain('Viewer');
    expect(serialized).not.toContain('Editor');
    expect(serialized).not.toContain('Owner');
  });
});
