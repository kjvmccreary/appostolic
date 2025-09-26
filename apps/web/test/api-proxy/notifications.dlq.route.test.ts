import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Mock } from 'vitest';
import type { NextRequest } from 'next/server';
import { GET as getDlq, POST as postDlqReplay } from '../../app/api-proxy/notifications/dlq/route';

vi.mock('../../src/lib/proxyHeaders', () => ({
  buildProxyHeaders: vi.fn(),
}));
vi.mock('../../src/lib/serverEnv', () => ({
  API_BASE: 'http://api.test',
}));
vi.mock('../../src/lib/roleGuard', () => ({
  requireTenantAdmin: vi.fn(),
}));

import { buildProxyHeaders } from '../../src/lib/proxyHeaders';
import type { ProxyHeaders } from '../../src/lib/proxyHeaders';
import { requireTenantAdmin } from '../../src/lib/roleGuard';

function makeReq(url = 'http://localhost:3000/api-proxy/notifications/dlq'): NextRequest {
  return { nextUrl: new URL(url) } as unknown as NextRequest;
}
function makePostReq(
  url = 'http://localhost:3000/api-proxy/notifications/dlq',
  body = '{}',
): NextRequest {
  return { nextUrl: new URL(url), text: async () => body } as unknown as NextRequest;
}

describe('api-proxy/notifications/dlq', () => {
  const originalFetch = global.fetch;
  let fetchMock: Mock<[RequestInfo | URL, RequestInit?], Promise<Response>>;

  beforeEach(() => {
    vi.resetModules();
    fetchMock = vi.fn();
    global.fetch = fetchMock as unknown as typeof fetch;
    vi.mocked(buildProxyHeaders).mockReset();
    vi.mocked(requireTenantAdmin).mockReset();
  });

  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('GET returns guard response when unauthorized', async () => {
    vi.mocked(requireTenantAdmin).mockResolvedValue(new Response('Unauthorized', { status: 401 }));
    const res = await getDlq(makeReq());
    expect(res.status).toBe(401);
  });

  it('GET proxies to API with headers and forwards X-Total-Count', async () => {
    vi.mocked(requireTenantAdmin).mockResolvedValue(null);
    vi.mocked(buildProxyHeaders).mockResolvedValue({
      Authorization: 'Bearer tenant-token',
    } as ProxyHeaders);
    fetchMock.mockResolvedValue(
      new Response('[{"id":"1"}]', {
        status: 200,
        headers: { 'content-type': 'application/json', 'x-total-count': '42' },
      }),
    );

    const res = await getDlq(
      makeReq('http://localhost:3000/api-proxy/notifications/dlq?take=10&skip=20&status=Failed'),
    );
    expect(fetchMock).toHaveBeenCalledWith(
      'http://api.test/api/notifications/dlq?take=10&skip=20&status=Failed',
      expect.objectContaining({
        method: 'GET',
        headers: expect.objectContaining({ Authorization: 'Bearer tenant-token' }),
      }),
    );
    expect(res.status).toBe(200);
    expect(res.headers.get('x-total-count')).toBe('42');
  });

  it('POST returns guard response when unauthorized', async () => {
    vi.mocked(requireTenantAdmin).mockResolvedValue(new Response('Forbidden', { status: 403 }));
    const res = await postDlqReplay(makePostReq());
    expect(res.status).toBe(403);
  });

  it('POST proxies body to API replay endpoint', async () => {
    vi.mocked(requireTenantAdmin).mockResolvedValue(null);
    vi.mocked(buildProxyHeaders).mockResolvedValue({
      Authorization: 'Bearer tenant-token',
      'Content-Type': 'application/json',
    } as ProxyHeaders);
    fetchMock.mockResolvedValue(
      new Response('{"requeued":1}', {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );

    const body = JSON.stringify({
      ids: ['a', 'b'],
      limit: 50,
      status: 'Failed',
      kind: 'Verification',
    });
    const res = await postDlqReplay(
      makePostReq('http://localhost:3000/api-proxy/notifications/dlq', body),
    );

    expect(fetchMock).toHaveBeenCalledWith(
      'http://api.test/api/notifications/dlq/replay',
      expect.objectContaining({
        method: 'POST',
        headers: expect.objectContaining({ Authorization: 'Bearer tenant-token' }),
        body,
      }),
    );
    expect(res.status).toBe(200);
  });
});
