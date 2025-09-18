import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render } from '@testing-library/react';
import { getServerSession } from 'next-auth';

// Mock server headers and navigation
vi.mock('next/headers', () => ({
  cookies: () => ({ get: () => undefined }),
}));
vi.mock('next/navigation', () => ({ redirect: vi.fn() }));

vi.mock('next-auth', () => ({ getServerSession: vi.fn() }));

async function importPage() {
  const mod = await import('./page');
  return mod.default as () => Promise<JSX.Element>;
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const getServerSessionMock = getServerSession as unknown as { mockResolvedValue: (v: any) => void };

describe('/studio/admin/settings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders 403 for non-admin membership (no TenantAdmin flag)', async () => {
    const session = {
      user: { email: 'user@example.com' },
      tenant: 'acme',
      memberships: [{ tenantSlug: 'acme', role: 'viewer', roles: [] }],
    };
    getServerSessionMock.mockResolvedValue(session);
    const Page = await importPage();
    const ui = await Page();
    const { getByText } = render(ui);
    expect(getByText(/403/i)).toBeInTheDocument();
  });

  it('renders heading for membership with TenantAdmin flag', async () => {
    const session = {
      user: { email: 'user@example.com' },
      tenant: 'acme',
      memberships: [{ tenantSlug: 'acme', role: 'viewer', roles: ['TenantAdmin'] }],
    };
    getServerSessionMock.mockResolvedValue(session);
    const Page = await importPage();
    const ui = await Page();
    const { getByText } = render(ui);
    expect(getByText(/Tenant Settings/i)).toBeInTheDocument();
  });

  // Legacy Owner/Admin roles no longer implicitly grant admin; must include TenantAdmin flag.

  it('resolves tenantId in session.tenant to matching membership slug using roles flags', async () => {
    const session = {
      user: { email: 'user@example.com' },
      tenant: 't-123',
      memberships: [
        { tenantId: 't-123', tenantSlug: 'acme', role: 'viewer', roles: ['TenantAdmin'] },
        { tenantId: 't-999', tenantSlug: 'other', role: 'viewer', roles: [] },
      ],
    };
    getServerSessionMock.mockResolvedValue(session);
    const Page = await importPage();
    const ui = await Page();
    const { getByText } = render(ui);
    expect(getByText(/Tenant Settings — acme/i)).toBeInTheDocument();
  });
});
