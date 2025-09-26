import { describe, it, expect, vi } from 'vitest';
vi.mock('../../../../src/lib/roleGuard', () => ({ requireCanCreate: vi.fn() }));
import { NextRequest } from 'next/server';

// Ensure env is set before importing modules that read it
process.env.NEXT_PUBLIC_API_BASE = process.env.NEXT_PUBLIC_API_BASE ?? 'http://localhost:5198';
process.env.WEB_AUTH_ENABLED = process.env.WEB_AUTH_ENABLED ?? 'true';

async function importRoute() {
  const mod = await import('./route');
  return {
    PUT: mod.PUT as (req: NextRequest, ctx: { params: { id: string } }) => Promise<Response>,
    DELETE: mod.DELETE as (req: NextRequest, ctx: { params: { id: string } }) => Promise<Response>,
  };
}
async function importProxyHeaders() {
  const mod = await import('../../../../src/lib/proxyHeaders');
  return mod as typeof import('../../../../src/lib/proxyHeaders');
}

// PUT/DELETE should 403 when canCreate is false

describe('/api-proxy/agents/[id]', () => {
  it('PUT returns 403 when session lacks canCreate', async () => {
    const ph = await importProxyHeaders();
    const hdrs = {
      Authorization: 'Bearer test-token',
      'Content-Type': 'application/json',
    } satisfies Awaited<ReturnType<typeof ph.buildProxyHeaders>>;
    vi.spyOn(ph, 'buildProxyHeaders').mockResolvedValue(hdrs);
    const rg = await import('../../../../src/lib/roleGuard');
    vi.mocked(rg.requireCanCreate).mockResolvedValue(new Response('Forbidden', { status: 403 }));

    const url = new URL('http://localhost:3000/api-proxy/agents/abc');
    const baseReq = new Request(url, { method: 'PUT', body: JSON.stringify({ name: 'A' }) });
    const req = new NextRequest(baseReq);
    const { PUT } = await importRoute();
    const res = await PUT(req, { params: { id: 'abc' } });
    expect(res.status).toBe(403);
  });

  it('DELETE returns 403 when session lacks canCreate', async () => {
    const ph = await importProxyHeaders();
    const hdrs = {
      Authorization: 'Bearer test-token',
      'Content-Type': 'application/json',
    } satisfies Awaited<ReturnType<typeof ph.buildProxyHeaders>>;
    vi.spyOn(ph, 'buildProxyHeaders').mockResolvedValue(hdrs);
    const rg = await import('../../../../src/lib/roleGuard');
    vi.mocked(rg.requireCanCreate).mockResolvedValue(new Response('Forbidden', { status: 403 }));

    const url = new URL('http://localhost:3000/api-proxy/agents/abc');
    const baseReq = new Request(url, { method: 'DELETE' });
    const req = new NextRequest(baseReq);
    const { DELETE } = await importRoute();
    const res = await DELETE(req, { params: { id: 'abc' } });
    expect(res.status).toBe(403);
  });
});
