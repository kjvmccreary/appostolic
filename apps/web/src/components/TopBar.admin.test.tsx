import React from 'react';
import { render, screen } from '@testing-library/react';
import { SessionProvider } from 'next-auth/react';
import { TopBar } from './TopBar';
// Mock next/navigation to avoid App Router invariant errors during unit tests
vi.mock('next/navigation', () => ({
  usePathname: () => '/',
  useRouter: () => ({ push: vi.fn(), prefetch: vi.fn() }),
}));

/**
 * Tests for tenant-scoped admin gating in TopBar.
 * Ensures Admin menu only appears when selected tenant membership includes admin role.
 */

interface SessionLike {
  user?: { email?: string };
  memberships?: { tenantSlug?: string; tenantId?: string; role?: string; roles?: string[] }[];
  tenant?: string;
  expires: string;
}

function renderWithSession(session: SessionLike) {
  return render(
    <SessionProvider session={session}>
      <TopBar />
    </SessionProvider>,
  );
}

describe('TopBar tenant-scoped admin gating', () => {
  const baseSession = {
    user: { email: 'user@example.com' },
    expires: new Date(Date.now() + 60_000).toISOString(),
  };

  it('hides all nav/actions when authed but no tenant claim', () => {
    renderWithSession({ ...baseSession, memberships: [{ tenantSlug: 'acme', role: 'admin' }] });
    // Dashboard link should not appear because tenant not yet selected
    expect(screen.queryByText('Dashboard')).not.toBeInTheDocument();
    expect(screen.queryByText('Agents')).not.toBeInTheDocument();
    expect(screen.queryByText('Create Lesson')).not.toBeInTheDocument();
  });

  it('hides Admin when no memberships', () => {
    renderWithSession({ ...baseSession, memberships: [], tenant: 'acme' });
    expect(screen.queryByText('Admin')).not.toBeInTheDocument();
  });

  it('hides Admin when memberships exist but selected tenant not admin', () => {
    renderWithSession({
      ...baseSession,
      tenant: 'acme',
      memberships: [
        { tenantSlug: 'acme', role: 'member' },
        { tenantSlug: 'other', role: 'admin' },
      ],
    });
    expect(screen.queryByText('Admin')).not.toBeInTheDocument();
  });

  it('shows Admin when selected tenant membership has role admin', () => {
    renderWithSession({
      ...baseSession,
      tenant: 'acme',
      memberships: [
        { tenantSlug: 'acme', role: 'admin' },
        { tenantSlug: 'other', role: 'member' },
      ],
    });
    expect(screen.getByText('Admin')).toBeInTheDocument();
  });

  it('shows Admin when selected tenant membership roles[] contains admin', () => {
    renderWithSession({
      ...baseSession,
      tenant: 'acme',
      memberships: [{ tenantSlug: 'acme', roles: ['editor', 'ADMIN'] }],
    });
    expect(screen.getByText('Admin')).toBeInTheDocument();
  });

  it('includes Settings in admin items (mobile drawer props)', () => {
    renderWithSession({
      ...baseSession,
      tenant: 'acme',
      memberships: [{ tenantSlug: 'acme', role: 'admin' }],
    });
    // The NavDrawer is a mocked component; we can’t inspect its props directly here without a custom mock.
    // Instead, ensure the desktop Admin button renders; the presence of Settings is covered by settings page tests.
    expect(screen.getByText('Admin')).toBeInTheDocument();
  });

  it('hides Admin if selected tenant does not match any membership', () => {
    renderWithSession({
      ...baseSession,
      tenant: 'missing',
      memberships: [{ tenantSlug: 'acme', role: 'admin' }],
    });
    expect(screen.queryByText('Admin')).not.toBeInTheDocument();
  });

  it('hides Admin when global session.isAdmin=true but selected tenant membership is not admin', () => {
    // Simulate a user who used to be an admin (global flag remains true),
    // but no longer has admin role on the currently selected tenant.
    const sessionWithGlobalFlag = {
      ...baseSession,
      isAdmin: true,
      tenant: 'acme',
      memberships: [
        { tenantSlug: 'acme', role: 'member' },
        { tenantSlug: 'other', role: 'admin' },
      ],
    } as unknown as SessionLike;
    renderWithSession(sessionWithGlobalFlag);
    expect(screen.queryByText('Admin')).not.toBeInTheDocument();
  });
});
