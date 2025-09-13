import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Mock } from 'vitest';
import { POST as postAccept } from '../../app/api-proxy/invites/accept/route';
import type { NextRequest } from 'next/server';
import { buildProxyHeaders } from '../../src/lib/proxyHeaders';
import type { ProxyHeaders } from '../../src/lib/proxyHeaders';

// Mock buildProxyHeaders to control auth behavior
vi.mock('../../src/lib/proxyHeaders', () => ({
  buildProxyHeaders: vi.fn(),
}));

// Mock serverEnv
vi.mock('../../src/lib/serverEnv', () => ({
  API_BASE: 'http://api.test',
}));

function makeReq(
  url = 'http://localhost:3000/api-proxy/invites/accept',
  body: string = '{}',
): NextRequest {
  const req = {
    text: async () => body,
    nextUrl: new URL(url),
  } as unknown as NextRequest;
  return req;
}

describe('api-proxy/invites/accept POST', () => {
  const originalFetch = global.fetch;
  let fetchMock: Mock<[RequestInfo | URL, RequestInit?], Promise<Response>>;

  beforeEach(() => {
    vi.resetModules();
    vi.mocked(buildProxyHeaders).mockReset();
    fetchMock = vi.fn();
    fetchMock.mockResolvedValue(
      new Response('{}', { status: 200, headers: { 'content-type': 'application/json' } }),
    );
    global.fetch = fetchMock as unknown as typeof fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('allows user-only auth (no tenant) and proxies', async () => {
    // tenant omitted; route passes requireTenant:false
    vi.mocked(buildProxyHeaders).mockResolvedValue({
      'x-dev-user': 'user@example.com',
      'Content-Type': 'application/json',
    } as ProxyHeaders);

    const res = await postAccept(makeReq());
    expect(res.status).toBe(200);
    expect(fetchMock).toHaveBeenCalledWith(
      'http://api.test/api/invites/accept',
      expect.objectContaining({
        method: 'POST',
        headers: expect.objectContaining({ 'x-dev-user': 'user@example.com' }),
      }),
    );
    // ensure x-tenant was not required/sent
    const [, opts] = fetchMock.mock.calls[0];
    const hdrs = (opts?.headers ?? {}) as Record<string, string>;
    expect(Object.prototype.hasOwnProperty.call(hdrs, 'x-tenant')).toBe(false);
  });

  it('returns 401 when no session (even permissive)', async () => {
    vi.mocked(buildProxyHeaders).mockResolvedValue(null as unknown as ProxyHeaders);

    const res = await postAccept(makeReq());
    expect(res.status).toBe(401);
  });
});
