import React from 'react';
import { render, screen } from '@testing-library/react';
import { TenantAwareTopBar } from './TenantAwareTopBar';
import { SessionProvider } from 'next-auth/react';

// Minimal session shape for tests; not importing full next-auth types to keep test lightweight.
interface SessionLike {
  memberships?: { tenant: string }[];
  tenant?: string;
  user?: { email?: string };
}

interface TestSession extends SessionLike {
  expires: string;
}

function renderWithSession(session: SessionLike) {
  const sess: TestSession = {
    ...session,
    expires: new Date(Date.now() + 60_000).toISOString(),
  };
  return render(
    <SessionProvider session={sess}>
      <TenantAwareTopBar />
    </SessionProvider>,
  );
}

describe('TenantAwareTopBar', () => {
  it('hides TopBar when multi-tenant and no selection', () => {
    renderWithSession({ memberships: [{ tenant: 't1' }, { tenant: 't2' }] });
    expect(screen.queryByRole('banner')).not.toBeInTheDocument();
  });

  it('shows TopBar when single tenant (auto context)', () => {
    renderWithSession({ memberships: [{ tenant: 't1' }] });
    expect(screen.getByRole('banner')).toBeInTheDocument();
  });

  it('shows TopBar when multi-tenant but selection present in session', () => {
    renderWithSession({ memberships: [{ tenant: 't1' }, { tenant: 't2' }], tenant: 't1' });
    expect(screen.getByRole('banner')).toBeInTheDocument();
  });
});
