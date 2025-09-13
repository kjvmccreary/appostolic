import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { NextRequest } from 'next/server';

// Seed env for middleware
process.env.WEB_AUTH_ENABLED = 'true';
process.env.AUTH_SECRET = process.env.AUTH_SECRET ?? 'test-secret';

// Mock next-auth/jwt getToken
vi.mock('next-auth/jwt', () => ({
  getToken: vi.fn(),
}));

// Import after mocks
import { middleware } from '../middleware';
import { getToken } from 'next-auth/jwt';

function makeReq(path: string): NextRequest {
  const url = new URL(`http://localhost:3000${path}`);
  return { nextUrl: url } as unknown as NextRequest;
}

describe('middleware route protection', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('redirects unauthenticated users to /login?next=...', async () => {
    vi.mocked(getToken).mockResolvedValue(null);
    const res = await middleware(makeReq('/studio/agents'));
    // Assert redirect Location header
    expect(res.headers.get('location')).toMatch('/login?next=%2Fstudio%2Fagents');
  });

  it('redirects authenticated users away from /login to /studio/agents', async () => {
    vi.mocked(getToken).mockResolvedValue({ email: 'u@example.com' } as unknown as Record<
      string,
      unknown
    >);
    const res = await middleware(makeReq('/login'));
    expect(res.headers.get('location')).toBe('/studio/agents');
  });
});
