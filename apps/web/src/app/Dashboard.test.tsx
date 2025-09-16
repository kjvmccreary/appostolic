import { describe, it, expect, vi, beforeEach } from 'vitest';

// The root page is now a server component that performs a redirect based on auth state.
// We mock next-auth's getServerSession and next/navigation's redirect to assert behavior.

vi.mock('next-auth', () => ({
  getServerSession: vi.fn(),
}));

class RedirectError extends Error {
  constructor(url: string) {
    super(`REDIRECT:${url}`);
    this.name = 'NEXT_REDIRECT';
  }
}

// Capture redirect calls and throw to simulate Next's redirect short-circuit
vi.mock('next/navigation', () => ({
  redirect: (url: string) => {
    throw new RedirectError(url);
  },
}));

import RootPage from '../../app/page';
import { getServerSession } from 'next-auth';

describe('RootPage redirects', () => {
  beforeEach(() => {
    (getServerSession as unknown as { mockReset: () => void }).mockReset?.();
  });

  it('redirects unauthenticated users to /login', async () => {
    (
      getServerSession as unknown as { mockResolvedValue: (v: unknown) => void }
    ).mockResolvedValue?.(null);
    await expect(RootPage()).rejects.toThrow(/REDIRECT:\/login/);
  });

  it('redirects authenticated users to /studio', async () => {
    (
      getServerSession as unknown as { mockResolvedValue: (v: unknown) => void }
    ).mockResolvedValue?.({
      user: { email: 'user@example.com' },
    });
    await expect(RootPage()).rejects.toThrow(/REDIRECT:\/studio/);
  });
});
