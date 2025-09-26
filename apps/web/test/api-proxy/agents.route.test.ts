import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Mock } from 'vitest';
import { GET as getAgents } from '../../app/api-proxy/agents/route';
import type { NextRequest } from 'next/server';
import { buildProxyHeaders } from '../../src/lib/proxyHeaders';
import type { ProxyHeadersContext } from '../../src/lib/proxyHeaders';

// Mock buildProxyHeaders to control auth behavior
vi.mock('../../src/lib/proxyHeaders', async () => {
  const actual = await vi.importActual<typeof import('../../src/lib/proxyHeaders')>(
    '../../src/lib/proxyHeaders',
  );
  return {
    ...actual,
    buildProxyHeaders: vi.fn(),
  };
});

// Mock serverEnv
vi.mock('../../src/lib/serverEnv', () => ({
  API_BASE: 'http://api.test',
}));

// Utility to build a minimal NextRequest-like object
function makeReq(url = 'http://localhost:3000/api-proxy/agents'): NextRequest {
  const req = { nextUrl: new URL(url) } as unknown as NextRequest;
  return req;
}

describe('api-proxy/agents GET', () => {
  const originalFetch = global.fetch;
  let fetchMock: Mock<[RequestInfo | URL, RequestInit?], Promise<Response>>;

  beforeEach(() => {
    vi.resetModules();
    vi.mocked(buildProxyHeaders).mockReset();
    fetchMock = vi.fn();
    fetchMock.mockResolvedValue(new Response(null, { status: 200 }));
    global.fetch = fetchMock as unknown as typeof fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('returns 401 when no session/headers', async () => {
    vi.mocked(buildProxyHeaders).mockResolvedValue(null as unknown as ProxyHeadersContext);
    const res = await getAgents(makeReq());
    expect(res.status).toBe(401);
  });

  it('proxies to API when authorized', async () => {
    vi.mocked(buildProxyHeaders).mockResolvedValue({
      headers: {
        Authorization: 'Bearer tenant-token',
        'Content-Type': 'application/json',
      },
      cookies: [],
    } as ProxyHeadersContext);
    fetchMock.mockResolvedValue(
      new Response('ok', { status: 200, headers: { 'content-type': 'application/json' } }),
    );
    const res = await getAgents(makeReq());
    expect(global.fetch).toHaveBeenCalledWith('http://api.test/api/agents', {
      method: 'GET',
      headers: expect.any(Object),
    });
    const [, opts] = fetchMock.mock.calls[0];
    expect((opts?.headers as Record<string, string>).Authorization).toBe('Bearer tenant-token');
    expect(res.status).toBe(200);
  });
});
