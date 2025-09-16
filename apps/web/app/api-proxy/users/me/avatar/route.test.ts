import { describe, it, expect, vi } from 'vitest';

// Ensure env is set before imports
process.env.NEXT_PUBLIC_API_BASE = process.env.NEXT_PUBLIC_API_BASE ?? 'http://localhost:5198';
process.env.WEB_AUTH_ENABLED = process.env.WEB_AUTH_ENABLED ?? 'true';

async function importRoute() {
  const mod = await import('./route');
  return mod.POST as (req: Request) => Promise<Response>;
}
async function importProxyHeaders() {
  const mod = await import('../../../../../src/lib/proxyHeaders');
  return mod as typeof import('../../../../../src/lib/proxyHeaders');
}

describe('/api-proxy/users/me/avatar', () => {
  it('returns 401 when unauthorized', async () => {
    const ph = await importProxyHeaders();
    vi.spyOn(ph, 'buildProxyHeaders').mockResolvedValue(null);
    const url = new URL('http://localhost:3000/api-proxy/users/me/avatar');
    const req = new Request(url, { method: 'POST' });
    const POST = await importRoute();
    const res = await POST(req);
    expect(res.status).toBe(401);
  });

  it('forwards multipart body and returns upstream status', async () => {
    const ph = await importProxyHeaders();
    vi.spyOn(ph, 'buildProxyHeaders').mockResolvedValue({
      'x-dev-user': 'test@example.com',
      'x-tenant': 't1',
    });

    // Mock upstream fetch
    const resp = { avatar: { url: '/media/users/u/avatar.png', mime: 'image/png' } };
    global.fetch = vi.fn(async () => {
      return new Response(JSON.stringify(resp), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      });
    });

    // Build a fake multipart/form-data request
    const fd = new FormData();
    fd.set('file', new Blob(['fake'], { type: 'image/png' }), 'a.png');
    // Provide a minimal stub for Request implementing formData()
    const fakeReq = { formData: async () => fd } as unknown as Request;
    const POST = await importRoute();
    const res = await POST(fakeReq);
    expect(res.status).toBe(200);
    expect(global.fetch).toHaveBeenCalledOnce();
  });
});
