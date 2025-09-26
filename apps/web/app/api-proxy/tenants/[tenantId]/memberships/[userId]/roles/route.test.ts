import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Mock } from 'vitest';
import type { NextRequest } from 'next/server';
import { POST } from './route';

vi.mock('../../../../../../../src/lib/roleGuard', () => ({ requireTenantAdmin: vi.fn() }));
vi.mock('../../../../../../../src/lib/proxyHeaders', async () => {
  const actual = await vi.importActual<typeof import('../../../../../../../src/lib/proxyHeaders')>(
    '../../../../../../../src/lib/proxyHeaders',
  );
  return {
    ...actual,
    buildProxyHeaders: vi.fn(),
  };
});
vi.mock('../../../../../../../src/lib/serverEnv', () => ({ API_BASE: 'http://api' }));

import { requireTenantAdmin } from '../../../../../../../src/lib/roleGuard';
import { buildProxyHeaders } from '../../../../../../../src/lib/proxyHeaders';
import type { ProxyHeadersContext } from '../../../../../../../src/lib/proxyHeaders';

function makeReq(body: string): NextRequest {
  const req = {
    text: async () => body,
    nextUrl: new URL('http://localhost/app/api-proxy/tenants/t1/memberships/u1/roles'),
  } as unknown as NextRequest;
  return req;
}

describe('proxy: memberships roles POST', () => {
  const originalFetch = global.fetch;
  let fetchMock: Mock<[RequestInfo | URL, RequestInit?], Promise<Response>>;

  beforeEach(() => {
    vi.resetAllMocks();
    fetchMock = vi.fn();
    global.fetch = fetchMock as unknown as typeof fetch;
  });
  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('denies non-admin via guard', async () => {
    vi.mocked(requireTenantAdmin).mockResolvedValue(new Response('Forbidden', { status: 403 }));
    const res = await POST(makeReq('{"roles":["TenantAdmin"]}'), {
      params: { tenantId: 't1', userId: 'u1' },
    });
    expect(res.status).toBe(403);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('returns 401 when no headers', async () => {
    vi.mocked(requireTenantAdmin).mockResolvedValue(null);
    vi.mocked(buildProxyHeaders).mockResolvedValue(null);
    const res = await POST(makeReq('{"roles":[]}'), { params: { tenantId: 't1', userId: 'u1' } });
    expect(res.status).toBe(401);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('forwards to API when authorized', async () => {
    vi.mocked(requireTenantAdmin).mockResolvedValue(null);
    vi.mocked(buildProxyHeaders).mockResolvedValue({
      headers: {
        Authorization: 'Bearer fake-token',
        'Content-Type': 'application/json',
      },
      cookies: [],
    } as ProxyHeadersContext);
    fetchMock.mockResolvedValue(
      new Response('ok', { status: 200, headers: { 'Content-Type': 'application/json' } }),
    );
    const payload = JSON.stringify({ roles: ['TenantAdmin', 'Creator'] });
    const res = await POST(makeReq(payload), { params: { tenantId: 't1', userId: 'u1' } });
    expect(res.status).toBe(200);
    expect(global.fetch).toHaveBeenCalledWith(
      'http://api/api/tenants/t1/memberships/u1/roles',
      expect.objectContaining({ method: 'POST', body: payload }),
    );
  });
});
