import { describe, it, expect, vi, beforeEach } from 'vitest';

process.env.NEXT_PUBLIC_API_BASE = process.env.NEXT_PUBLIC_API_BASE ?? 'http://localhost:5198';
process.env.WEB_AUTH_ENABLED = process.env.WEB_AUTH_ENABLED ?? 'true';

async function importRoute() {
  const mod = await import('./route');
  return mod.GET;
}
async function importProxyHeaders() {
  const mod = await import('../../../../src/lib/proxyHeaders');
  return mod as typeof import('../../../../src/lib/proxyHeaders');
}

describe('/api-proxy/metadata/denominations', () => {
  beforeEach(() => {
    vi.resetModules();
    vi.clearAllMocks();
  });

  it('returns 401 when proxy headers are unavailable', async () => {
    const ph = await importProxyHeaders();
    vi.spyOn(ph, 'buildProxyHeaders').mockResolvedValue(
      null as unknown as Awaited<ReturnType<typeof ph.buildProxyHeaders>>,
    );
    const GET = await importRoute();
    const res = await GET();
    expect(res.status).toBe(401);
  });

  it('requests denominations with tenantless proxy context', async () => {
    const ph = await importProxyHeaders();
    const headers = { Authorization: 'Bearer token' } as Record<string, string>;
    const buildSpy = vi.spyOn(ph, 'buildProxyHeaders').mockResolvedValue({
      headers,
      cookies: [],
    });
    const payload = JSON.stringify([{ id: 'd1', name: 'Denomination' }]);
    global.fetch = vi.fn(
      async () =>
        new Response(payload, {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
    );
    const GET = await importRoute();
    const res = await GET();
    expect(res.status).toBe(200);
    expect(buildSpy).toHaveBeenCalledWith({ requireTenant: false });
    const text = await res.text();
    expect(text).toContain('Denomination');
  });
});
