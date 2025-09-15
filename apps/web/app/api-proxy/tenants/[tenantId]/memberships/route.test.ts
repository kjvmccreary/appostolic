import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Mock } from 'vitest';
import type { NextRequest } from 'next/server';
import { GET } from './route';

vi.mock('../../../../../src/lib/roleGuard', () => ({ guardProxyRole: vi.fn() }));
vi.mock('../../../../../src/lib/proxyHeaders', () => ({ buildProxyHeaders: vi.fn() }));
vi.mock('../../../../../src/lib/serverEnv', () => ({ API_BASE: 'http://api' }));

import { guardProxyRole } from '../../../../../src/lib/roleGuard';
import { buildProxyHeaders } from '../../../../../src/lib/proxyHeaders';
import type { ProxyHeaders } from '../../../../../src/lib/proxyHeaders';

function makeReq(url = 'http://localhost/app/api-proxy/tenants/t1/memberships'): NextRequest {
  // Minimal NextRequest-like shape used by our handler (only params are read, not req itself)
  return { nextUrl: new URL(url) } as unknown as NextRequest;
}

describe('proxy: memberships list', () => {
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
    vi.mocked(guardProxyRole).mockResolvedValue(new Response('Forbidden', { status: 403 }));
    const res = await GET(makeReq(), { params: { tenantId: 't1' } });
    expect(res.status).toBe(403);
  });

  it('forwards to API with headers', async () => {
    vi.mocked(guardProxyRole).mockResolvedValue(null);
    vi.mocked(buildProxyHeaders).mockResolvedValue({
      'x-dev-user': 'dev@example.com',
      'x-tenant': 't1',
      'Content-Type': 'application/json',
    } as ProxyHeaders);
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify([{ userId: 'u1' }]), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    const res = await GET(makeReq(), { params: { tenantId: 't1' } });
    expect(res.status).toBe(200);
    expect(global.fetch).toHaveBeenCalledWith(
      'http://api/api/tenants/t1/memberships',
      expect.any(Object),
    );
  });
});
