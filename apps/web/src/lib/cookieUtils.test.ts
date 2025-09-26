import { describe, it, expect } from 'vitest';
import { extractSetCookieValues, parseSetCookie } from './cookieUtils';

describe('cookieUtils', () => {
  it('parses cookie attributes including flags', () => {
    const parsed = parseSetCookie(
      'rt=abc123; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=3600; Expires=Wed, 25 Oct 2028 10:00:00 GMT',
    );
    expect(parsed).not.toBeNull();
    expect(parsed?.name).toBe('rt');
    expect(parsed?.value).toBe('abc123');
    expect(parsed?.options.path).toBe('/');
    expect(parsed?.options.httpOnly).toBe(true);
    expect(parsed?.options.secure).toBe(true);
    expect(parsed?.options.sameSite).toBe('lax');
    expect(parsed?.options.maxAge).toBe(3600);
    expect(parsed?.options.expires).toBeInstanceOf(Date);
  });

  it('extracts multiple set-cookie headers from fallback string', () => {
    const fakeHeaders = {
      get: (name: string) =>
        name.toLowerCase() === 'set-cookie'
          ? 'foo=bar; Path=/, rt=xyz; Path=/; HttpOnly; Expires=Wed, 25 Oct 2028 10:00:00 GMT'
          : null,
    } as unknown as Headers;
    const values = extractSetCookieValues(fakeHeaders);
    expect(values).toHaveLength(2);
    expect(values[0]).toContain('foo=bar');
    expect(values[1]).toContain('rt=xyz');
  });
});
