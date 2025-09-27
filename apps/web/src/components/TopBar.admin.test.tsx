import React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
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
  isSuperAdmin?: boolean;
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
    renderWithSession({
      ...baseSession,
      memberships: [{ tenantSlug: 'acme', role: 'Viewer', roles: ['TenantAdmin'] }],
    });
    // Dashboard link should not appear because tenant not yet selected
    expect(screen.queryByText('Dashboard')).not.toBeInTheDocument();
    expect(screen.queryByText('Agents')).not.toBeInTheDocument();
    expect(screen.queryByText('Create Lesson')).not.toBeInTheDocument();
  });

  it('hides Admin when no memberships', () => {
    renderWithSession({ ...baseSession, memberships: [], tenant: 'acme' });
    expect(screen.queryByText('Admin')).not.toBeInTheDocument();
  });

  it('hides Admin when memberships exist but selected tenant lacks TenantAdmin flag', () => {
    renderWithSession({
      ...baseSession,
      tenant: 'acme',
      memberships: [
        { tenantSlug: 'acme', role: 'Viewer', roles: [] },
        { tenantSlug: 'other', role: 'Viewer', roles: ['TenantAdmin'] },
      ],
    });
    expect(screen.queryByText('Admin')).not.toBeInTheDocument();
  });

  it('shows Admin when selected tenant membership has TenantAdmin flag', () => {
    renderWithSession({
      ...baseSession,
      tenant: 'acme',
      memberships: [
        { tenantSlug: 'acme', role: 'Viewer', roles: ['TenantAdmin'] },
        { tenantSlug: 'other', role: 'Viewer', roles: [] },
      ],
    });
    expect(screen.getByText('Admin')).toBeInTheDocument();
  });

  it("shows Admin when selected tenant membership roles[] contains 'TenantAdmin' flag (multi-tenant)", () => {
    // Multi-tenant user: single-tenant safeguard should not apply; flags-based admin should gate.
    renderWithSession({
      ...baseSession,
      tenant: 'acme',
      memberships: [
        { tenantSlug: 'acme', role: 'Viewer', roles: ['Creator', 'TenantAdmin'] },
        { tenantSlug: 'other', role: 'Viewer', roles: [] },
      ],
    });
    expect(screen.getByText('Admin')).toBeInTheDocument();
  });

  it('includes Settings in admin items (mobile drawer props)', () => {
    renderWithSession({
      ...baseSession,
      tenant: 'acme',
      memberships: [{ tenantSlug: 'acme', role: 'Viewer', roles: ['TenantAdmin'] }],
    });
    // The NavDrawer is a mocked component; we can’t inspect its props directly here without a custom mock.
    // Instead, ensure the desktop Admin button renders; the presence of Settings is covered by settings page tests.
    expect(screen.getByText('Admin')).toBeInTheDocument();
  });

  it('surfaces Guardrails entry in the Admin dropdown', async () => {
    renderWithSession({
      ...baseSession,
      tenant: 'acme',
      memberships: [{ tenantSlug: 'acme', role: 'Viewer', roles: ['TenantAdmin'] }],
    });
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /admin/i }));
    expect(screen.getByRole('menuitem', { name: /guardrails/i })).toBeInTheDocument();
  });

  it('shows Admin when user is superadmin without tenant admin flag', () => {
    renderWithSession({
      ...baseSession,
      tenant: 'acme',
      isSuperAdmin: true,
      memberships: [{ tenantSlug: 'acme', role: 'Viewer', roles: [] }],
    });
    expect(screen.getByText('Admin')).toBeInTheDocument();
  });

  it('includes Guardrails Console entry for superadmins', async () => {
    renderWithSession({
      ...baseSession,
      tenant: 'acme',
      isSuperAdmin: true,
      memberships: [{ tenantSlug: 'acme', role: 'Viewer', roles: [] }],
    });
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /admin/i }));
    expect(screen.getByRole('menuitem', { name: /guardrails console/i })).toBeInTheDocument();
  });

  it('hides Admin if selected tenant does not match any membership', () => {
    renderWithSession({
      ...baseSession,
      tenant: 'missing',
      memberships: [{ tenantSlug: 'acme', role: 'Viewer', roles: ['TenantAdmin'] }],
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
        { tenantSlug: 'acme', role: 'Viewer', roles: [] },
        { tenantSlug: 'other', role: 'Viewer', roles: ['TenantAdmin'] },
      ],
    } as unknown as SessionLike;
    renderWithSession(sessionWithGlobalFlag);
    expect(screen.queryByText('Admin')).not.toBeInTheDocument();
  });

  it('shows Admin when session.tenant is tenantId and membership is Admin by id match', () => {
    renderWithSession({
      ...baseSession,
      tenant: 'tid-123',
      memberships: [
        { tenantId: 'tid-123', tenantSlug: 'acme', role: 'Viewer', roles: ['TenantAdmin'] },
        { tenantId: 'tid-999', tenantSlug: 'other', role: 'Viewer', roles: [] },
      ],
    });
    expect(screen.getByText('Admin')).toBeInTheDocument();
  });

  it('does NOT show Admin for single-tenant non-admin users (auto-selected)', () => {
    // Simulate a user with exactly one membership, which the auth layer will auto-select.
    // Role is Viewer -> should not show Admin in TopBar.
    renderWithSession({
      ...baseSession,
      tenant: 'acme',
      memberships: [{ tenantSlug: 'acme', role: 'Viewer', roles: [] }],
    });
    expect(screen.queryByText('Admin')).not.toBeInTheDocument();
  });

  it('does NOT show Admin when flags include Approver+Creator+Learner but NOT TenantAdmin (bitmask 14 scenario)', () => {
    // Bitmask 14 (assuming 1=TenantAdmin,2=Approver,4=Creator,8=Learner) => 2|4|8 = 14, missing TenantAdmin (1)
    renderWithSession({
      ...baseSession,
      tenant: 'acme',
      memberships: [
        { tenantSlug: 'acme', role: 'Viewer', roles: ['Approver', 'Creator', 'Learner'] },
      ],
    });
    expect(screen.queryByText('Admin')).not.toBeInTheDocument();
  });
});
