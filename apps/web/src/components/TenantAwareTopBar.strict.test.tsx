import React from 'react';
import { render, screen } from '@testing-library/react';
import { SessionProvider } from 'next-auth/react';
import { TenantAwareTopBar } from './TenantAwareTopBar';

interface SessionLike {
  user?: { email?: string };
  memberships?: { tenantSlug?: string; tenantId?: string }[];
  tenant?: string;
  expires: string;
}

function withSession(session: SessionLike) {
  return (
    <SessionProvider session={session}>
      <TenantAwareTopBar />
    </SessionProvider>
  );
}

const baseSession = {
  user: { email: 'user@example.com' },
  memberships: [{ tenantSlug: 'alpha' }, { tenantSlug: 'beta' }],
  expires: new Date(Date.now() + 60_000).toISOString(),
};

describe('TenantAwareTopBar strict cookie gating', () => {
  beforeEach(() => {
    // Reset document.cookie for each test
    Object.defineProperty(document, 'cookie', {
      writable: true,
      value: '',
    });
  });

  it('hides when no selected_tenant cookie even if session.tenant present', () => {
    const session = { ...baseSession, tenant: 'alpha' };
    render(withSession(session));
    expect(screen.queryByRole('banner')).not.toBeInTheDocument();
  });

  it('hides when cookie value not in memberships', () => {
    document.cookie = 'selected_tenant=gamma';
    render(withSession(baseSession));
    expect(screen.queryByRole('banner')).not.toBeInTheDocument();
  });

  it('shows when cookie matches membership slug', () => {
    document.cookie = 'selected_tenant=beta';
    render(withSession(baseSession));
    expect(screen.queryByRole('banner')).toBeInTheDocument();
  });
});
