import { describe, it, expect, vi, beforeEach, beforeAll, afterAll } from 'vitest';
import { NextRequest } from 'next/server';

vi.mock('next/headers', () => {
  const store = new Map<string, { value: string; options?: Record<string, unknown> }>();
  const api = {
    get: (name: string) => {
      const entry = store.get(name);
      return entry ? { name, value: entry.value } : undefined;
    },
    set: (name: string, value: string, options?: Record<string, unknown>) => {
      store.set(name, { value, options });
    },
    delete: (name: string) => {
      store.delete(name);
    },
    getAll: () =>
      Array.from(store.entries()).map(([key, entry]) => ({ name: key, value: entry.value })),
    has: (name: string) => store.has(name),
    toString: () =>
      Array.from(store.entries())
        .map(([key, entry]) => `${key}=${entry.value}`)
        .join('; '),
    clear: () => {
      store.clear();
    },
  };
  return {
    cookies: () => api,
  };
});

import { cookies } from 'next/headers';
import { registerRotation, getRotation } from '../../../../src/lib/refreshRotationBridge';
import { POST, GET } from './route';

// Helper to build NextRequest
function buildRequest(url: string, init?: RequestInit) {
  const base = new Request(url, init);
  return new NextRequest(base);
}

const fetchMock = vi.fn<[RequestInfo | URL, RequestInit?], Promise<Response>>();

beforeAll(() => {
  vi.stubGlobal('fetch', fetchMock);
});

afterAll(() => {
  vi.unstubAllGlobals();
});

// Mock getServerSession by monkeypatching the imported module logic indirectly.
vi.mock('../../../../src/lib/auth', () => ({ authOptions: {} }));
vi.mock('next-auth', async (orig) => {
  const actualMod = (await (orig as () => Promise<unknown>)()) as Record<string, unknown>;
  return {
    ...actualMod,
    getServerSession: vi.fn(async () => ({
      memberships: [
        { tenantId: 't-123', tenantSlug: 'kevin-personal-2' },
        { tenantId: 't-456', tenantSlug: 'org-alpha' },
      ],
    })),
  } as Record<string, unknown>;
});

// Access the mocked getServerSession to adjust behavior in specific tests
import { getServerSession } from 'next-auth';

describe('/api/tenant/select', () => {
  beforeEach(() => {
    (getServerSession as unknown as { mockClear: () => void }).mockClear();
    const jar = cookies() as unknown as { clear?: () => void };
    jar.clear?.();
    const payload = JSON.stringify({
      access: { token: 'mock-neutral', expiresAt: Date.now() + 60000 },
    });
    fetchMock.mockReset();
    fetchMock.mockResolvedValue(
      new Response(payload, {
        status: 200,
        headers: {
          'content-type': 'application/json',
          'set-cookie': 'rt=rotated-token; Path=/; HttpOnly; SameSite=Lax',
        },
      }),
    );
    Reflect.set(globalThis, '__appRefreshRotationBridge', undefined);
  });

  it('POST rejects missing tenant', async () => {
    const req = buildRequest('http://localhost/api/tenant/select', {
      method: 'POST',
      body: JSON.stringify({}),
    });
    const res = await POST(req);
    expect(res.status).toBe(400);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('POST accepts matching tenant slug', async () => {
    const req = buildRequest('http://localhost/api/tenant/select', {
      method: 'POST',
      body: JSON.stringify({ tenant: 'kevin-personal-2' }),
      headers: { cookie: 'rt=existing-token; selected_tenant=kevin-personal-3' },
    });
    const res = await POST(req);
    expect(res.status).toBe(200);
    const json = await res.json();
    expect(json).toMatchObject({ ok: true, tenant: 'kevin-personal-2' });
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toContain('/api/auth/refresh');
  });

  it('POST rejects unknown tenant', async () => {
    const req = buildRequest('http://localhost/api/tenant/select', {
      method: 'POST',
      body: JSON.stringify({ tenant: 'does-not-exist' }),
    });
    const res = await POST(req);
    expect(res.status).toBe(400);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('POST adopts rotated refresh token from bridge before rotating again', async () => {
    const legacyToken = 'legacy-token';
    const bridgedToken = 'bridged-token';
    const rotatedToken = 'rotated-token';
    fetchMock.mockResolvedValueOnce(
      new Response(
        JSON.stringify({
          access: { token: 'mock-neutral', expiresAt: Date.now() + 60000 },
        }),
        {
          status: 200,
          headers: {
            'content-type': 'application/json',
            'set-cookie': `rt=${rotatedToken}; Path=/; HttpOnly; SameSite=Lax`,
          },
        },
      ),
    );
    registerRotation(legacyToken, {
      name: 'rt',
      value: bridgedToken,
      options: { path: '/', httpOnly: true, sameSite: 'lax' },
    });
    const req = buildRequest('http://localhost/api/tenant/select', {
      method: 'POST',
      body: JSON.stringify({ tenant: 'kevin-personal-2' }),
      headers: { cookie: `rt=${legacyToken}; selected_tenant=kevin-personal-3` },
    });
    const res = await POST(req);
    expect(res.status).toBe(200);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [, init] = fetchMock.mock.calls[0] ?? [];
    const headers = (init?.headers as Record<string, string>) ?? {};
    expect(headers.cookie).toContain(`rt=${bridgedToken}`);
    expect(getRotation(bridgedToken)?.value).toBe(rotatedToken);
  });

  it('GET redirects and sets cookie for valid tenant', async () => {
    const req = buildRequest(
      'http://localhost/api/tenant/select?tenant=org-alpha&next=/studio/agents',
    );
    const res = await GET(req);
    expect(res.status).toBe(307); // NextResponse.redirect default
    const cookie = res.cookies.get('selected_tenant');
    expect(cookie?.value).toBe('org-alpha');
  });

  it('GET rejects invalid tenant', async () => {
    const req = buildRequest('http://localhost/api/tenant/select?tenant=bad-slug');
    const res = await GET(req);
    expect(res.status).toBe(400);
  });
});
