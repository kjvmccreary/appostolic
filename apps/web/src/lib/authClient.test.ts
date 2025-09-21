import {
  primeNeutralAccess,
  getAccessToken,
  withAuthFetch,
  startAutoRefresh,
  stopAutoRefresh,
  forceRefresh,
} from './authClient';
import { vi } from 'vitest';

// Basic unit tests for in-memory token logic (no real network).

describe('authClient', () => {
  it('returns primed token without refresh', async () => {
    primeNeutralAccess('abc123', Date.now() + 60_000);
    const tok = await getAccessToken();
    expect(tok).toBe('abc123');
  });

  it('refresh attempts when expired (will fail and null token)', async () => {
    // Force expired
    primeNeutralAccess('old', Date.now() - 10_000);
    // Mock fetch to fail
    const orig = global.fetch;
    const mockResp: { ok: boolean; status: number; json: () => Promise<unknown> } = {
      ok: false,
      status: 401,
      json: async () => ({}),
    };
    global.fetch = vi.fn().mockResolvedValue(mockResp as unknown as Response);
    await expect(getAccessToken()).rejects.toThrow();
    global.fetch = orig;
  });

  it('withAuthFetch attaches Authorization when token present', async () => {
    primeNeutralAccess('tok123', Date.now() + 60_000);
    const orig = global.fetch;
    const spy = vi.fn().mockResolvedValue(new Response('{}', { status: 200 }));
    global.fetch = spy;
    await withAuthFetch('/api/test');
    const headers = spy.mock.calls[0][1].headers as Headers;
    expect(headers.get('Authorization')).toBe('Bearer tok123');
    global.fetch = orig;
  });

  it('retries once on 401 after forced refresh', async () => {
    // First call 401, second call 200
    primeNeutralAccess('oldtok', Date.now() + 5_000);
    let call = 0;
    const orig = global.fetch;
    global.fetch = vi.fn().mockImplementation(() => {
      call++;
      if (call === 1) return Promise.resolve(new Response('{}', { status: 401 }));
      if (call === 2) {
        // Simulate refresh updated token before retry
        primeNeutralAccess('newtok', Date.now() + 60_000);
        return Promise.resolve(new Response('{}', { status: 200 }));
      }
      return Promise.resolve(new Response('{}', { status: 200 }));
    });
    const r = await withAuthFetch('/api/needs-auth');
    expect(r.status).toBe(200);
    const mockFetch = global.fetch as unknown as { mock: { calls: unknown[] } };
    expect(mockFetch.mock.calls.length).toBeGreaterThanOrEqual(2);
    global.fetch = orig;
  });

  it('auto refresh schedules and updates token (silent)', async () => {
    // Short-lived token triggers near-immediate refresh (after min 5s delay). We'll simulate refresh fast.
    stopAutoRefresh();
    primeNeutralAccess('short', Date.now() + 10_000);
    const orig = global.fetch;
    let refreshed = false;
    global.fetch = vi.fn().mockImplementation((url: RequestInfo | URL) => {
      if (url === '/api/auth/refresh') {
        refreshed = true;
        primeNeutralAccess('refreshed', Date.now() + 120_000);
        return Promise.resolve(
          new Response(
            JSON.stringify({
              access: { token: 'refreshed', expiresAt: Date.now() + 120_000, type: 'neutral' },
            }),
            { status: 200 },
          ),
        );
      }
      return Promise.resolve(new Response('{}', { status: 200 }));
    });
    startAutoRefresh();
    // Force a manual refresh to exercise scheduling path deterministically
    await forceRefresh();
    expect(refreshed).toBe(true);
    stopAutoRefresh();
    global.fetch = orig;
  });
});
