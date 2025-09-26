import { describe, it, expect, vi } from 'vitest';
import { NextRequest } from 'next/server';

// Ensure env is set before importing modules that read it
process.env.NEXT_PUBLIC_API_BASE = process.env.NEXT_PUBLIC_API_BASE ?? 'http://localhost:5198';
process.env.WEB_AUTH_ENABLED = process.env.WEB_AUTH_ENABLED ?? 'true';

async function importRoute() {
  const mod = await import('./route');
  return mod.GET as (req: NextRequest) => Promise<Response>;
}
async function importPost() {
  const mod = await import('./route');
  return mod.POST as (req: NextRequest) => Promise<Response>;
}
async function importProxyHeaders() {
  const mod = await import('../../../src/lib/proxyHeaders');
  return mod as typeof import('../../../src/lib/proxyHeaders');
}

describe('/api-proxy/agent-tasks', () => {
  it('returns 401 when no headers (unauthenticated)', async () => {
    const ph = await importProxyHeaders();
    const spy = vi
      .spyOn(ph, 'buildProxyHeaders')
      .mockResolvedValue(null as unknown as Awaited<ReturnType<typeof ph.buildProxyHeaders>>);
    const url = new URL('http://localhost:3000/api-proxy/agent-tasks');
    const req = new NextRequest(url);
    const GET = await importRoute();
    const res = await GET(req);
    expect(res.status).toBe(401);
    spy.mockRestore();
  });

  it('returns 200 when headers present (mocks fetch)', async () => {
    const headers = {
      Authorization: 'Bearer test-token',
      'Content-Type': 'application/json',
    } as const;
    const ph = await importProxyHeaders();
    const spy = vi
      .spyOn(ph, 'buildProxyHeaders')
      .mockResolvedValue({ headers, cookies: [] } as Awaited<
        ReturnType<typeof ph.buildProxyHeaders>
      >);

    const body = JSON.stringify({ items: [{ id: 'task1', status: 'Pending' }] });
    global.fetch = vi.fn(
      async () =>
        new Response(body, { status: 200, headers: { 'content-type': 'application/json' } }),
    );

    const url = new URL('http://localhost:3000/api-proxy/agent-tasks');
    const req = new NextRequest(url);
    const GET = await importRoute();
    const res = await GET(req);
    expect(res.status).toBe(200);
    const text = await res.text();
    expect(text).toContain('task1');
    spy.mockRestore();
  });

  it('POST forwards body and returns Location header', async () => {
    const headers = {
      Authorization: 'Bearer test-token',
      'Content-Type': 'application/json',
    } as const;
    const ph = await importProxyHeaders();
    const spy = vi
      .spyOn(ph, 'buildProxyHeaders')
      .mockResolvedValue({ headers, cookies: [] } as Awaited<
        ReturnType<typeof ph.buildProxyHeaders>
      >);

    const created = { id: '123', status: 'Pending' };
    global.fetch = vi.fn(
      async () =>
        new Response(JSON.stringify(created), {
          status: 201,
          headers: { 'content-type': 'application/json', location: '/api/agent-tasks/123' },
        }),
    );

    const url = new URL('http://localhost:3000/api-proxy/agent-tasks');
    const baseReq = new Request(url, {
      method: 'POST',
      body: JSON.stringify({ agentId: 'a', input: { q: 'hi' } }),
      headers: { 'content-type': 'application/json' },
    });
    const req = new NextRequest(baseReq);
    const POST = await importPost();
    const res = await POST(req);
    expect(res.status).toBe(201);
    expect(res.headers.get('location')).toBe('/api/agent-tasks/123');
    const text = await res.text();
    expect(text).toContain('123');
    spy.mockRestore();
  });
});
