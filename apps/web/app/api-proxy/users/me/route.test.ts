import { GET } from './route';
import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest';

vi.mock('../../../../src/lib/proxyHeaders', () => ({
  buildProxyHeaders: vi.fn(),
}));
vi.mock('../../../../src/lib/serverEnv', () => ({
  API_BASE: 'http://api.test',
}));

import { buildProxyHeaders } from '../../../../src/lib/proxyHeaders';
import type { ProxyHeaders } from '../../../../src/lib/proxyHeaders';

// Minimal proxy test: ensures GET handler passes through JSON body.
// We mock global.fetch to simulate upstream API response.

describe('/api-proxy/users/me GET', () => {
  const originalFetch = global.fetch;
  beforeEach(() => {
    vi.mocked(buildProxyHeaders).mockResolvedValue({
      Authorization: 'Bearer token',
    } as ProxyHeaders);
    global.fetch = vi.fn(async () => {
      return new Response(JSON.stringify({ email: 'user@example.com', profile: {} }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      });
    }) as unknown as typeof fetch;
  });
  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('returns JSON user profile', async () => {
    interface ResShape {
      status: number;
      json: () => Promise<{ email: string; profile: object }>;
    }
    const res = (await GET()) as unknown as ResShape;
    expect(res.status).toBe(200);
    const json = await res.json();
    expect(json.email).toBe('user@example.com');
  });
});
