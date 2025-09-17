import { describe, it, expect } from 'vitest';
import { NextRequest } from 'next/server';
import { middleware } from './middleware';

// Helper to build a mocked NextRequest with cookie header
function buildRequest(path: string, options: { cookie?: string } = {}) {
  const url = `http://localhost${path}`;
  const headers = new Headers();
  if (options.cookie) headers.set('cookie', options.cookie);
  // next-auth jwt decoding is bypassed because WEB_AUTH_ENABLED is false by default (middleware short-circuits)
  return new NextRequest(url, { headers });
}

describe('middleware redirect gating (dev mode short-circuit awareness)', () => {
  it('does not redirect when WEB_AUTH_ENABLED is false (default)', async () => {
    const req = buildRequest('/studio/agents');
    const res = await middleware(req as any);
    // In dev short-circuit path we expect a plain pass-through response
    expect(res.headers.get('x-pathname')).toBe('/studio/agents');
  });
});

// Full auth-enabled scenarios would require setting WEB_AUTH_ENABLED=true and mocking getToken.
// Those are higher complexity and can be added later with a test harness mocking next-auth/jwt.
