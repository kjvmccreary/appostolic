import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render } from '@testing-library/react';
import React from 'react';
import { getServerSession } from 'next-auth';

// We will mock useSession + signOut + router
vi.mock('next-auth/react', () => ({
  useSession: vi.fn(),
  signOut: vi.fn(async () => undefined),
}));
vi.mock('next/navigation', () => ({
  useRouter: () => ({ replace: vi.fn() }),
}));

// Mock getServerSession used in layout
vi.mock('next-auth', () => ({ getServerSession: vi.fn() }));

// Mock TopBar component (we only care if it's rendered) & Providers wrapper
vi.mock('../../src/components/TopBar', () => ({ TopBar: () => <div data-testid="topbar" /> }));
vi.mock('../providers', () => ({
  __esModule: true,
  default: (p: { children: React.ReactNode }) => <>{p.children}</>,
}));

// Helper to dynamically import layout each test after mocks set
async function importLayout() {
  const mod = await import('../layout');
  return mod.default as (p: { children: React.ReactNode }) => Promise<JSX.Element>;
}

// Access mocked getServerSession (vi.mock applied above)
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const getServerSessionMock = getServerSession as unknown as { mockResolvedValue: (v: any) => void };

// NOTE: This is a lightweight test: we assert that when session lacks tenant, TopBar is not rendered.

interface SessionLike {
  user?: { email?: string };
  memberships?: { tenantId: string; tenantSlug: string; role?: string }[];
  tenant?: string;
  expires?: string;
}

describe('logout / login multi-tenant flow', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('does not render TopBar when multi-tenant user logs in without selection after prior logout', async () => {
    // Simulate fresh login: session has memberships (2) but no tenant claim.
    const mockSession: SessionLike = {
      user: { email: 'user@example.com' },
      memberships: [
        { tenantId: 't1', tenantSlug: 'alpha', role: 'member' },
        { tenantId: 't2', tenantSlug: 'beta', role: 'member' },
      ],
      // no tenant claim to simulate fresh multi-tenant login with selection required
      expires: new Date(Date.now() + 60_000).toISOString(),
    };
    getServerSessionMock.mockResolvedValue(mockSession);
    const Layout = await importLayout();
    const ui = await Layout({ children: <div /> });
    const { queryByTestId } = render(ui);
    expect(queryByTestId('topbar')).toBeNull();
  });
});
