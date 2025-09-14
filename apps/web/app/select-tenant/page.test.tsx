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

  it('auto-selects when only one membership and redirects to /studio/agents by default', async () => {
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
    expect(dest).toContain('/api/tenant/select?');
    expect(dest).toContain('tenant=t1-personal');
    expect(dest).toContain('next=%2Fstudio%2Fagents');
  });
});
