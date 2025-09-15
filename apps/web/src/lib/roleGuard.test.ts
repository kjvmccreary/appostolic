import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { Session } from 'next-auth';

vi.mock('next-auth', async () => {
  const actual = await vi.importActual<typeof import('next-auth')>('next-auth');
  return { ...actual, getServerSession: vi.fn() };
});
vi.mock('./auth', () => ({ authOptions: {} }));
vi.mock('next/headers', () => ({ cookies: () => ({ get: () => ({ value: 'acme' }) }) }));

import { getServerSession } from 'next-auth';
import { guardByFlags } from './roleGuard';

function sessionWith(
  memberships: Array<{ tenantId: string; tenantSlug: string; role: string }>,
): Session {
  return { user: { email: 'u@example.com' }, memberships } as unknown as Session;
}

describe('guardByFlags', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('returns 401 when not signed in', async () => {
    vi.mocked(getServerSession).mockResolvedValue(null);
    const res = await guardByFlags({ require: 'isAdmin' });
    expect(res?.status).toBe(401);
  });

  it('returns 403 when tenant cannot be resolved', async () => {
    vi.mocked(getServerSession).mockResolvedValue(sessionWith([]));
    // Force no cookie and no id/slug
    vi.doMock('next/headers', () => ({ cookies: () => ({ get: () => undefined }) }));
    const res = await guardByFlags({ require: 'isAdmin' });
    expect(res?.status).toBe(403);
  });

  it('returns 403 when not admin for Owner/Admin requirement', async () => {
    vi.mocked(getServerSession).mockResolvedValue(
      sessionWith([{ tenantId: 't1', tenantSlug: 'acme', role: 'Viewer' }]),
    );
    const res = await guardByFlags({ tenantIdOrSlug: { id: 't1' }, require: 'isAdmin' });
    expect(res?.status).toBe(403);
  });

  it('allows when admin via id resolution', async () => {
    vi.mocked(getServerSession).mockResolvedValue(
      sessionWith([{ tenantId: 't1', tenantSlug: 'acme', role: 'Owner' }]),
    );
    const res = await guardByFlags({ tenantIdOrSlug: { id: 't1' }, require: 'isAdmin' });
    expect(res).toBeNull();
  });

  it('allows canCreate for Editor-derived role', async () => {
    vi.mocked(getServerSession).mockResolvedValue(
      sessionWith([{ tenantId: 't1', tenantSlug: 'acme', role: 'Editor' }]),
    );
    const res = await guardByFlags({ tenantIdOrSlug: { slug: 'acme' }, require: 'canCreate' });
    expect(res).toBeNull();
  });
});
