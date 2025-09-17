import { describe, it, expect, vi, beforeEach } from 'vitest';
import SelectTenantPage from './page';
import { GET as apiGet } from '../api/tenant/select/route';
import { NextRequest } from 'next/server';

// Helper to build NextRequest
function buildRequest(url: string) {
  const base = new Request(url);
  return new NextRequest(base);
}

// Mock auth plumbing so the page sees exactly one membership
vi.mock('../../src/lib/auth', () => ({ authOptions: {} }));
vi.mock('next-auth', async (orig) => {
  const actual = (await (orig as () => Promise<Record<string, unknown>>)()) as Record<
    string,
    unknown
  >;
  return {
    ...actual,
    getServerSession: vi.fn(async () => ({
      user: { email: 'single@user.test' },
      memberships: [{ tenantId: 't1', tenantSlug: 't1-personal', role: 'Admin' }],
    })),
  } as Record<string, unknown>;
});

// Capture redirect destination thrown by next/navigation.redirect mock
interface RedirectError extends Error {
  destination?: string;
}
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

// Provide cookies() mock for the page (it reads cookieTenant but won't set in this flow)
vi.mock('next/headers', () => ({ cookies: () => ({ get: vi.fn() }) }));

// Access mocks
import { getServerSession } from 'next-auth';

describe('Single-tenant login flow', () => {
  beforeEach(() => {
    (getServerSession as unknown as { mockClear: () => void }).mockClear?.();
  });

  it('page redirects to API route which sets cookie and continues', async () => {
    // 1) Render the page and capture redirect to API route with encoded next
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

    // 2) Simulate the API route handling the redirect by setting the cookie
    const req = buildRequest(`http://localhost${dest}`);
    const res = await apiGet(req);
    expect(res.status).toBe(307);
    const cookie = res.cookies.get('selected_tenant');
    expect(cookie?.value).toBe('t1-personal');
  });
});
