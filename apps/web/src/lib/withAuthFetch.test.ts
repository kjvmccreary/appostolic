import { primeNeutralAccess, withAuthFetch } from './authClient';
import { vi } from 'vitest';

describe('withAuthFetch integration', () => {
  it('attaches bearer token', async () => {
    primeNeutralAccess('abcTok', Date.now() + 60_000);
    const orig = global.fetch;
    const spy = vi.fn().mockResolvedValue(new Response('{}', { status: 200 }));
    global.fetch = spy as unknown as typeof fetch;
    await withAuthFetch('/api/demo', { method: 'GET' });
    const init = spy.mock.calls[0][1];
    const headers = (init.headers as Headers) || new Headers();
    expect(headers.get('Authorization')).toBe('Bearer abcTok');
    global.fetch = orig;
  });
});
