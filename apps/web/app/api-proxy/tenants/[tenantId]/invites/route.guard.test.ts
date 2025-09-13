import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Mock } from 'vitest';
import { GET as getInvites, POST as postInvite } from './route';
import { POST as resendInvite, DELETE as revokeInvite } from './[email]/route';
import type { NextRequest } from 'next/server';
import { buildProxyHeaders } from '../../../../../src/lib/proxyHeaders';
import { guardProxyRole } from '../../../../../src/lib/roleGuard';

vi.mock('../../../../../src/lib/proxyHeaders', () => ({ buildProxyHeaders: vi.fn() }));
vi.mock('../../../../../src/lib/roleGuard', () => ({ guardProxyRole: vi.fn() }));
vi.mock('../../../../../src/lib/serverEnv', () => ({ API_BASE: 'http://api.test' }));

function makeReq(url: string): NextRequest {
  return { nextUrl: new URL(url), text: async () => '{}' } as unknown as NextRequest;
}

describe('tenants invites proxy guards', () => {
  const originalFetch = global.fetch;
  let fetchMock: Mock<[RequestInfo | URL, RequestInit?], Promise<Response>>;

  beforeEach(() => {
    vi.resetModules();
    vi.mocked(buildProxyHeaders).mockReset();
    vi.mocked(guardProxyRole).mockReset();
    fetchMock = vi.fn();
    global.fetch = fetchMock as unknown as typeof fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('GET/POST invite list/create are guarded', async () => {
    vi.mocked(guardProxyRole).mockResolvedValue(new Response('Forbidden', { status: 403 }));
    const res1 = await getInvites(makeReq('http://x/api-proxy/tenants/t1/invites'), {
      params: { tenantId: 't1' },
    } as unknown as { params: { tenantId: string } });
    expect(res1.status).toBe(403);
    const res2 = await postInvite(makeReq('http://x/api-proxy/tenants/t1/invites'), {
      params: { tenantId: 't1' },
    } as unknown as { params: { tenantId: string } });
    expect(res2.status).toBe(403);
  });

  it('resend/revoke are guarded', async () => {
    vi.mocked(guardProxyRole).mockResolvedValue(new Response('Forbidden', { status: 403 }));
    const resResend = await resendInvite(makeReq('http://x'), {
      params: { tenantId: 't1', email: 'e@example.com' },
    } as unknown as { params: { tenantId: string; email: string } });
    expect(resResend.status).toBe(403);
    const resRevoke = await revokeInvite(makeReq('http://x'), {
      params: { tenantId: 't1', email: 'e@example.com' },
    } as unknown as { params: { tenantId: string; email: string } });
    expect(resRevoke.status).toBe(403);
  });

  it('proxies when guard passes and headers exist', async () => {
    vi.mocked(guardProxyRole).mockResolvedValue(null);
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

    const res = await getInvites(makeReq('http://x/api-proxy/tenants/t1/invites'), {
      params: { tenantId: 't1' },
    } as unknown as { params: { tenantId: string } });
    expect(res.status).toBe(200);
    expect(fetchMock).toHaveBeenCalled();
  });
});
