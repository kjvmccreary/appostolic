import { describe, it, expect, vi, beforeEach } from 'vitest';
import { NextRequest } from 'next/server';

vi.mock('../../../../src/lib/roleGuard', () => ({ requireTenantAdmin: vi.fn() }));

process.env.NEXT_PUBLIC_API_BASE = process.env.NEXT_PUBLIC_API_BASE ?? 'http://localhost:5198';
process.env.WEB_AUTH_ENABLED = process.env.WEB_AUTH_ENABLED ?? 'true';

async function importRoute() {
  const mod = await import('./route');
  return { GET: mod.GET, PUT: mod.PUT };
}
async function importProxyHeaders() {
  const mod = await import('../../../../src/lib/proxyHeaders');
  return mod as typeof import('../../../../src/lib/proxyHeaders');
}
async function importRoleGuard() {
  const mod = await import('../../../../src/lib/roleGuard');
  return mod as typeof import('../../../../src/lib/roleGuard');
}

describe('/api-proxy/tenants/settings', () => {
  beforeEach(() => {
    vi.resetModules();
    vi.clearAllMocks();
  });

  it('returns guard response when user lacks admin rights', async () => {
    const rg = await importRoleGuard();
    vi.mocked(rg.requireTenantAdmin).mockResolvedValue(new Response('Forbidden', { status: 403 }));
    const { GET } = await importRoute();
    const res = await GET();
    expect(res.status).toBe(403);
  });

  it('returns 401 when proxy headers missing', async () => {
    const rg = await importRoleGuard();
    vi.mocked(rg.requireTenantAdmin).mockResolvedValue(null);
    const ph = await importProxyHeaders();
    vi.spyOn(ph, 'buildProxyHeaders').mockResolvedValue(
      null as unknown as Awaited<ReturnType<typeof ph.buildProxyHeaders>>,
    );
    const { GET } = await importRoute();
    const res = await GET();
    expect(res.status).toBe(401);
  });

  it('GET proxies upstream response', async () => {
    const rg = await importRoleGuard();
    vi.mocked(rg.requireTenantAdmin).mockResolvedValue(null);
    const ph = await importProxyHeaders();
    vi.spyOn(ph, 'buildProxyHeaders').mockResolvedValue({
      headers: { Authorization: 'Bearer token', 'Content-Type': 'application/json' },
      cookies: [],
    });
    const payload = JSON.stringify({ tenantId: 't1' });
    global.fetch = vi.fn(
      async () =>
        new Response(payload, {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
    );
    const { GET } = await importRoute();
    const res = await GET();
    expect(res.status).toBe(200);
    const text = await res.text();
    expect(text).toContain('t1');
  });

  it('PUT forwards body and headers', async () => {
    const rg = await importRoleGuard();
    vi.mocked(rg.requireTenantAdmin).mockResolvedValue(null);
    const ph = await importProxyHeaders();
    vi.spyOn(ph, 'buildProxyHeaders').mockResolvedValue({
      headers: { Authorization: 'Bearer token' },
      cookies: [],
    });
    const updated = { ok: true };
    const fetchSpy = vi.fn(
      async () =>
        new Response(JSON.stringify(updated), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
    );
    global.fetch = fetchSpy as unknown as typeof fetch;

    const url = new URL('http://localhost:3000/api-proxy/tenants/settings');
    const req = new NextRequest(
      new Request(url, {
        method: 'PUT',
        body: JSON.stringify({ displayName: 'Org' }),
        headers: { 'content-type': 'application/json' },
      }),
    );
    const { PUT } = await importRoute();
    const res = await PUT(req);
    expect(res.status).toBe(200);
    expect(fetchSpy).toHaveBeenCalledWith(
      'http://localhost:5198/api/tenants/settings',
      expect.objectContaining({ method: 'PUT' }),
    );
  });
});
