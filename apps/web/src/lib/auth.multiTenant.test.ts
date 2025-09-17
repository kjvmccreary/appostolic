import { authOptions } from './auth';
import type { JWT } from 'next-auth/jwt';
import type { Session, User as NextAuthUser } from 'next-auth';
import { describe, it, expect } from 'vitest';

/**
 * Ensures that when a user has multiple memberships the jwt callback no longer
 * auto-selects a tenant; tenant remains undefined until explicit selection.
 */

describe('auth jwt callback multi-tenant selection', () => {
  it('does not auto-select tenant when multiple memberships', async () => {
    const user: NextAuthUser & {
      memberships: { tenantId: string; tenantSlug: string; role: string }[];
    } = {
      id: 'u1',
      email: 'u@example.com',
      name: 'u@example.com',
      memberships: [
        { tenantId: 't1', tenantSlug: 'alpha', role: 'Admin' },
        { tenantId: 't2', tenantSlug: 'beta', role: 'Viewer' },
      ],
    } as NextAuthUser & { memberships: { tenantId: string; tenantSlug: string; role: string }[] };

    const token1 = await authOptions.callbacks!.jwt!({
      token: {} as JWT,
      user,
      account: null,
      profile: undefined,
      trigger: 'signIn',
    });
    expect((token1 as { tenant?: string }).tenant).toBeUndefined();

    // Explicit selection via update should set it
    const token2 = await authOptions.callbacks!.jwt!({
      token: token1 as JWT,
      user: undefined as unknown as NextAuthUser,
      account: null,
      profile: undefined,
      trigger: 'update',
      session: { tenant: 'alpha' } as unknown as Session,
    });
    expect((token2 as { tenant?: string }).tenant).toBe('alpha');
  });
});
