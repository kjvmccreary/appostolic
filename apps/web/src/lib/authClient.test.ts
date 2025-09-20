import { primeNeutralAccess, getAccessToken, withAuthFetch } from './authClient';
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
});
