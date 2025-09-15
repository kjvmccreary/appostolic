import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Mock } from 'vitest';
import { GET as getMembers } from './route';
import { PUT as putMember, DELETE as deleteMember } from './[userId]/route';
import type { NextRequest } from 'next/server';
import { buildProxyHeaders } from '../../../../../src/lib/proxyHeaders';
import { requireTenantAdmin } from '../../../../../src/lib/roleGuard';

vi.mock('../../../../../src/lib/proxyHeaders', () => ({ buildProxyHeaders: vi.fn() }));
vi.mock('../../../../../src/lib/roleGuard', () => ({ requireTenantAdmin: vi.fn() }));
vi.mock('../../../../../src/lib/serverEnv', () => ({ API_BASE: 'http://api.test' }));

function makeReq(url: string): NextRequest {
  return { nextUrl: new URL(url), text: async () => '{}' } as unknown as NextRequest;
}

describe('tenants members proxy guards', () => {
  const originalFetch = global.fetch;
  let fetchMock: Mock<[RequestInfo | URL, RequestInit?], Promise<Response>>;

  beforeEach(() => {
    vi.resetModules();
    vi.mocked(buildProxyHeaders).mockReset();
    vi.mocked(requireTenantAdmin).mockReset();
    fetchMock = vi.fn();
    global.fetch = fetchMock as unknown as typeof fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('returns 403 when role guard fails for list', async () => {
    vi.mocked(requireTenantAdmin).mockResolvedValue(new Response('Forbidden', { status: 403 }));
    const res = await getMembers(makeReq('http://x/api-proxy/tenants/t1/members'), {
      params: { tenantId: 't1' },
    } as unknown as { params: { tenantId: string } });
    expect(res.status).toBe(403);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('proxies when role guard passes and headers exist', async () => {
    vi.mocked(requireTenantAdmin).mockResolvedValue(null);
    vi.mocked(buildProxyHeaders).mockResolvedValue({
      'x-dev-user': 'u',
      'x-tenant': 't',
      'Content-Type': 'application/json',
    });
    fetchMock = vi
      .fn()
      .mockResolvedValue(
        new Response('[]', { status: 200, headers: { 'content-type': 'application/json' } }),
      );
    global.fetch = fetchMock as unknown as typeof fetch;

    const res = await getMembers(makeReq('http://x/api-proxy/tenants/t1/members'), {
      params: { tenantId: 't1' },
    } as unknown as { params: { tenantId: string } });
    expect(res.status).toBe(200);
    expect(fetchMock).toHaveBeenCalled();
  });

  it('PUT/DELETE are also guarded', async () => {
    vi.mocked(requireTenantAdmin).mockResolvedValue(new Response('Forbidden', { status: 403 }));
    const put = await putMember(makeReq('http://x'), {
      params: { tenantId: 't1', userId: 'u1' },
    } as unknown as { params: { tenantId: string; userId: string } });
    expect(put.status).toBe(403);
    const del = await deleteMember(makeReq('http://x'), {
      params: { tenantId: 't1', userId: 'u1' },
    } as unknown as { params: { tenantId: string; userId: string } });
    expect(del.status).toBe(403);
  });
});
