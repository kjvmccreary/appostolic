import { describe, it, expect, vi } from 'vitest';
import { NextRequest } from 'next/server';

// Ensure env is set before importing modules that read it
process.env.NEXT_PUBLIC_API_BASE = process.env.NEXT_PUBLIC_API_BASE ?? 'http://localhost:5198';
process.env.WEB_AUTH_ENABLED = process.env.WEB_AUTH_ENABLED ?? 'true';

// Import after env setup
// Use dynamic imports after env setup to avoid ESM import hoisting
async function importRoute() {
  const mod = await import('./route');
  return mod.GET as (req: NextRequest) => Promise<Response>;
}
async function importProxyHeaders() {
  const mod = await import('../../../src/lib/proxyHeaders');
  return mod as typeof import('../../../src/lib/proxyHeaders');
}

// Smoke test: 401 when unauthorized, 200 when authorized (mocked)

describe('/api-proxy/agents', () => {
  it('returns 401 when no headers (unauthenticated)', async () => {
    const ph = await importProxyHeaders();
    const spy = vi
      .spyOn(ph, 'buildProxyHeaders')
      .mockResolvedValue(null as unknown as Awaited<ReturnType<typeof ph.buildProxyHeaders>>);
    const url = new URL('http://localhost:3000/api-proxy/agents');
    const req = new NextRequest(url);
    const GET = await importRoute();
    const res = await GET(req);
    expect(res.status).toBe(401);
    spy.mockRestore();
  });

  it('returns 200 when headers present (mocks fetch)', async () => {
    const headers = {
      'x-dev-user': 'test@example.com',
      'x-tenant': 'kevin-personal',
      'Content-Type': 'application/json',
    } as const;
    const ph = await importProxyHeaders();
    const spy = vi
      .spyOn(ph, 'buildProxyHeaders')
      .mockResolvedValue(headers as unknown as Awaited<ReturnType<typeof ph.buildProxyHeaders>>);

    const body = JSON.stringify([{ id: 'a', name: 'Agent A' }]);
    global.fetch = vi.fn(
      async () =>
        new Response(body, { status: 200, headers: { 'content-type': 'application/json' } }),
    );

    const url = new URL('http://localhost:3000/api-proxy/agents');
    const req = new NextRequest(url);
    const GET = await importRoute();
    const res = await GET(req);
    expect(res.status).toBe(200);
    const text = await res.text();
    expect(text).toContain('Agent A');
    spy.mockRestore();
  });
});
