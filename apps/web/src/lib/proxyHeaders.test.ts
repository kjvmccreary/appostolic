import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import type { Mock } from 'vitest';
import type { CookieSetterOptions } from './cookieUtils';
import { getServerSession } from 'next-auth';

type Entry = { name: string; value: string; options: CookieSetterOptions };

class StubCookieStore {
  private map = new Map<string, Entry>();

  constructor(seed?: Record<string, Entry>) {
    if (seed) {
      for (const [name, entry] of Object.entries(seed)) {
        this.map.set(name, entry);
      }
    }
  }

  get(name: string) {
    const entry = this.map.get(name);
    if (!entry) return undefined;
    return { name, value: entry.value };
  }

  set(name: string, value: string, options: CookieSetterOptions = {}) {
    this.map.set(name, { name, value, options });
  }

  delete(name: string) {
    this.map.delete(name);
  }

  toString(): string {
    return Array.from(this.map.values())
      .map((entry) => `${entry.name}=${entry.value}`)
      .join('; ');
  }
}

function createStore(refreshToken: string): StubCookieStore {
  const options: CookieSetterOptions = { path: '/', httpOnly: true, sameSite: 'lax' };
  return new StubCookieStore({
    rt: { name: 'rt', value: refreshToken, options },
    selected_tenant: { name: 'selected_tenant', value: 'tenant-a', options },
    'next-auth.session-token': {
      name: 'next-auth.session-token',
      value: 'session-token',
      options,
    },
  });
}

const originalFetch = global.fetch;
const cookieStoreFactory: { current: StubCookieStore } = {
  current: createStore(''),
};

type HeaderMap = { get: (name: string) => string | null };

vi.mock('next/headers', () => {
  return {
    cookies: () => cookieStoreFactory.current,
    headers: () =>
      ({
        get: () => null,
      }) as HeaderMap,
  };
});

vi.mock('next-auth', () => {
  return {
    getServerSession: vi.fn().mockResolvedValue({
      user: { email: 'user@example.com' },
      tenant: 'tenant-a',
      memberships: [{ tenantSlug: 'tenant-a', tenantId: 'tenant-a-id', roles: ['TenantAdmin'] }],
    }),
  };
});

describe('buildProxyHeaders refresh rotation bridge', () => {
  const getServerSessionMock = getServerSession as unknown as Mock;

  beforeEach(() => {
    vi.resetModules();
    vi.clearAllMocks();
    Reflect.set(globalThis, '__appProxyTokenCache', undefined);
    Reflect.set(globalThis, '__appProxyInflight', undefined);
    Reflect.set(globalThis, '__appProxyRotationBridge', undefined);
    cookieStoreFactory.current = createStore('legacy-token');
    process.env.WEB_AUTH_ENABLED = 'true';
    process.env.NEXT_PUBLIC_API_BASE = 'http://localhost:5198';
    getServerSessionMock.mockResolvedValue({
      user: { email: 'user@example.com' },
      tenant: 'tenant-a',
      memberships: [{ tenantSlug: 'tenant-a', tenantId: 'tenant-a-id', roles: ['TenantAdmin'] }],
    });
  });

  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('reuses rotated refresh token for subsequent concurrent requests', async () => {
    const oldToken = 'legacy-token';
    const newToken = 'rotated-token';
    const now = Date.now();
    const fetchMock = vi.fn().mockImplementation(() => {
      const responseBody = JSON.stringify({
        access: { token: 'neutral-access', expiresAt: now + 600_000, type: 'neutral' },
        tenantToken: {
          access: { token: 'tenant-access', expiresAt: now + 600_000, type: 'tenant' },
        },
      });
      return Promise.resolve(
        new Response(responseBody, {
          status: 200,
          headers: {
            'content-type': 'application/json',
            'set-cookie': `rt=${newToken}; Path=/; HttpOnly; SameSite=Lax`,
          },
        }),
      );
    });
    global.fetch = fetchMock as unknown as typeof fetch;

    const { buildProxyHeaders } = await import('./proxyHeaders');
    const first = await buildProxyHeaders();
    expect(first).not.toBeNull();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(first?.cookies.find((c) => c.name === 'rt')?.value).toBe(newToken);

    cookieStoreFactory.current = createStore(oldToken);

    const second = await buildProxyHeaders({ requireTenant: false });
    expect(second).not.toBeNull();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const bridged = second?.cookies.find((c) => c.name === 'rt');
    expect(bridged?.value).toBe(newToken);
  });

  it('reuses cached tenant access when neutral scope lacks fresh token', async () => {
    const oldToken = 'legacy-token';
    const newToken = 'tenant-rotated-token';
    const now = Date.now();
    cookieStoreFactory.current = createStore(oldToken);

    const fetchMock = vi.fn().mockImplementation((input: RequestInfo | URL) => {
      const urlString = typeof input === 'string' ? input : (input as Request).url;
      const url = new URL(urlString);
      const tenant = url.searchParams.get('tenant');
      const responseBody = JSON.stringify({
        access: { token: tenant ? 'neutral-access' : 'neutral-access', expiresAt: now + 600_000 },
        tenantToken: tenant
          ? {
              access: { token: 'tenant-access', expiresAt: now + 600_000 },
            }
          : undefined,
      });
      return Promise.resolve(
        new Response(responseBody, {
          status: 200,
          headers: {
            'content-type': 'application/json',
            'set-cookie': `rt=${newToken}; Path=/; HttpOnly; SameSite=Lax`,
          },
        }),
      );
    });
    global.fetch = fetchMock as unknown as typeof fetch;

    const { buildProxyHeaders } = await import('./proxyHeaders');
    const tenantCtx = await buildProxyHeaders();
    expect(tenantCtx).not.toBeNull();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(tenantCtx?.headers.Authorization).toBe('Bearer tenant-access');

    const neutralCtx = await buildProxyHeaders({ requireTenant: false });
    expect(neutralCtx).not.toBeNull();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(neutralCtx?.headers.Authorization).toBe('Bearer neutral-access');
    const rotationCookie = neutralCtx?.cookies.find((c) => c.name === 'rt');
    expect(rotationCookie).toBeUndefined();
  });
});
