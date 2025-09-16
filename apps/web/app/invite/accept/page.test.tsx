import { describe, it, expect, vi, beforeEach } from 'vitest';
import { http, HttpResponse } from 'msw';

// MSW server from global
const server: import('msw/node').SetupServer = (globalThis as unknown as { __mswServer: unknown })
  .__mswServer as import('msw/node').SetupServer;

// Mock getServerSession to simulate signed in/out
vi.mock('next-auth', () => ({ getServerSession: vi.fn() }));
// Match the import specifiers used by the page under test
vi.mock('../../../src/lib/auth', () => ({ authOptions: {} }));
vi.mock('../../lib/serverFetch', () => ({
  fetchFromProxy: (input: string, init?: RequestInit) => fetch(`http://localhost${input}`, init),
}));

// Mock next/navigation redirect to throw an Error with destination we can assert
type RedirectError = Error & { destination?: string };
vi.mock('next/navigation', async (orig) => {
  const actual = (await (orig as () => Promise<Record<string, unknown>>)()) as Record<
    string,
    unknown
  >;
  return {
    ...actual,
    redirect: (url: string) => {
      const e: RedirectError = new Error('REDIRECT');
      e.destination = url;
      throw e;
    },
  } as Record<string, unknown>;
});

import AcceptInvitePage from './page';
import { getServerSession } from 'next-auth';

describe('/invite/accept page (server)', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('redirects to /login when not signed in and token provided', async () => {
    vi.mocked(getServerSession).mockResolvedValue(null);
    let destination = '';
    try {
      await (
        AcceptInvitePage as unknown as (args: {
          searchParams: { token?: string };
        }) => Promise<unknown>
      )({
        searchParams: { token: 'tok' },
      });
      expect(false, 'expected redirect').toBe(true);
    } catch (e) {
      const err = e as RedirectError;
      if (err.message === 'REDIRECT') {
        destination = err.destination ?? '';
      } else {
        throw e;
      }
    }
    expect(destination).toMatch(/\/login\?next=/);
    expect(decodeURIComponent(destination.split('=')[1]!)).toBe('/invite/accept?token=tok');
  });

  it('renders error when API returns non-OK', async () => {
    vi.mocked(getServerSession).mockResolvedValue({
      user: { email: 'u@example.com' },
    } as unknown as Parameters<typeof getServerSession>[0]);
    server.use(
      http.post('http://localhost/api-proxy/invites/accept', () =>
        HttpResponse.text('Bad token', { status: 400 }),
      ),
    );
    const jsx = (await (
      AcceptInvitePage as unknown as (args: {
        searchParams: { token?: string };
      }) => Promise<unknown>
    )({ searchParams: { token: 'bad' } })) as unknown as { [k: string]: unknown };
    // Cannot easily render server component here; assert structure via snapshot-like checks
    expect(JSON.stringify(jsx)).toContain('Invite error');
  });
});
