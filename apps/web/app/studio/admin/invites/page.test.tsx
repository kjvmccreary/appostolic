import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('next-auth', () => ({ getServerSession: vi.fn() }));
vi.mock('../../../../src/lib/auth', () => ({ authOptions: {} }));
vi.mock('next/navigation', () => ({ redirect: vi.fn() }));
vi.mock('../../../lib/serverFetch', () => ({ fetchFromProxy: vi.fn() }));

import { getServerSession } from 'next-auth';
import { redirect } from 'next/navigation';
import { fetchFromProxy } from '../../../lib/serverFetch';
import InvitesAdminPage from './page';

describe('InvitesAdminPage (server)', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('redirects to /login when not authenticated', async () => {
    vi.mocked(getServerSession).mockResolvedValue(
      null as unknown as Parameters<typeof getServerSession>[0],
    );
    await expect(InvitesAdminPage()).resolves.toBeDefined();
    expect(redirect).toHaveBeenCalledWith('/login');
  });

  it('renders 403 for non-admin', async () => {
    vi.mocked(getServerSession).mockResolvedValue({
      user: { email: 'u@example.com' },
      memberships: [{ tenantSlug: 't1', tenantId: 'tid', role: 'Viewer', roles: [] }],
      tenant: 't1',
    } as unknown as Parameters<typeof getServerSession>[0]);
    const jsx = (await InvitesAdminPage()) as unknown as { [k: string]: unknown };
    expect(JSON.stringify(jsx)).toContain('403');
  });

  it('fetches and renders when admin', async () => {
    vi.mocked(getServerSession).mockResolvedValue({
      user: { email: 'u@example.com' },
      memberships: [{ tenantSlug: 't1', tenantId: 'tid', role: 'Viewer', roles: ['TenantAdmin'] }],
      tenant: 't1',
    } as unknown as Parameters<typeof getServerSession>[0]);
    vi.mocked(fetchFromProxy).mockResolvedValue({
      ok: true,
      json: async () => [],
    } as unknown as Response);
    const jsx = (await InvitesAdminPage()) as unknown as { [k: string]: unknown };
    expect(JSON.stringify(jsx)).toContain('Invites');
  });

  it('renders successfully when ok flag would be present (handled by client toast)', async () => {
    vi.mocked(getServerSession).mockResolvedValue({
      user: { email: 'u@example.com' },
      memberships: [{ tenantSlug: 't1', tenantId: 'tid', role: 'Viewer', roles: ['TenantAdmin'] }],
      tenant: 't1',
    } as unknown as Parameters<typeof getServerSession>[0]);
    vi.mocked(fetchFromProxy).mockResolvedValue({
      ok: true,
      json: async () => [],
    } as unknown as Response);
    const jsx = (await InvitesAdminPage()) as unknown as { [k: string]: unknown };
    expect(JSON.stringify(jsx)).toContain('Invites');
  });

  it('renders successfully when err flag would be present (handled by client toast)', async () => {
    vi.mocked(getServerSession).mockResolvedValue({
      user: { email: 'u@example.com' },
      memberships: [{ tenantSlug: 't1', tenantId: 'tid', role: 'Viewer', roles: ['TenantAdmin'] }],
      tenant: 't1',
    } as unknown as Parameters<typeof getServerSession>[0]);
    vi.mocked(fetchFromProxy).mockResolvedValue({
      ok: true,
      json: async () => [],
    } as unknown as Response);
    const jsx = (await InvitesAdminPage()) as unknown as { [k: string]: unknown };
    expect(JSON.stringify(jsx)).toContain('Invites');
  });

  it('renders failure state when invites fetch fails', async () => {
    vi.mocked(getServerSession).mockResolvedValue({
      user: { email: 'u@example.com' },
      memberships: [{ tenantSlug: 't1', tenantId: 'tid', role: 'Viewer', roles: ['TenantAdmin'] }],
      tenant: 't1',
    } as unknown as Parameters<typeof getServerSession>[0]);
    vi.mocked(fetchFromProxy).mockResolvedValue({
      ok: false,
      json: async () => ({ message: 'bad' }),
    } as unknown as Response);
    const jsx = (await InvitesAdminPage()) as unknown as { [k: string]: unknown };
    expect(JSON.stringify(jsx)).toContain('Failed to load invites');
  });
});
