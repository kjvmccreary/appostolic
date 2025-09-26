import { primeNeutralAccess, withAuthFetch } from './authClient';
import { vi } from 'vitest';

describe('withAuthFetch integration', () => {
  it('attaches bearer token', async () => {
    primeNeutralAccess('abcTok', Date.now() + 120_000);
    const orig = global.fetch;
    const spy = vi.fn().mockResolvedValue(new Response('{}', { status: 200 }));
    global.fetch = spy as unknown as typeof fetch;
    await withAuthFetch('/api/demo', { method: 'GET' });
    const init = (spy.mock.calls[0][1] ?? {}) as RequestInit;
    const headersInit = init.headers ?? {};
    const headers = headersInit instanceof Headers ? headersInit : new Headers(headersInit);
    expect(headers.get('Authorization')).toBe('Bearer abcTok');
    global.fetch = orig;
  });
});
