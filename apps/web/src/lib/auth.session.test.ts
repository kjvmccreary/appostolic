import { authOptions } from './auth';
import type { Session, User as NextAuthUser } from 'next-auth';
import type { AdapterUser } from 'next-auth/adapters';
import type { JWT } from 'next-auth/jwt';
import type { LegacyRole } from './roles';

describe('NextAuth session derivation (roles-aware)', () => {
  it('derives booleans from memberships and selected tenant', async () => {
    type TestMembership = {
      tenantId: string;
      tenantSlug: string;
      role: LegacyRole;
      roles?: string[];
    };
    const u: NextAuthUser & { memberships: TestMembership[] } = {
      id: 'u1',
      email: 'a@example.com',
      name: 'a@example.com',
      memberships: [
        {
          tenantId: 'tid-1',
          tenantSlug: 'acme',
          role: 'Viewer',
          roles: ['TenantAdmin', 'Creator', 'Approver', 'Learner'],
        },
        { tenantId: 'tid-2', tenantSlug: 'beta', role: 'Viewer', roles: ['Learner'] },
      ],
    } as NextAuthUser & { memberships: TestMembership[] };

    // First jwt call with user on sign-in
    const token1 = await authOptions.callbacks!.jwt!({
      token: {} as JWT,
      user: u,
      account: null,
      profile: undefined,
      trigger: 'signIn',
    });

    // Update selected tenant to 'acme'
    const token2 = await authOptions.callbacks!.jwt!({
      token: token1 as JWT,
      user: undefined as unknown as NextAuthUser,
      account: null,
      profile: undefined,
      trigger: 'update',
      session: { tenant: 'acme' } as unknown as Session,
    });

    // Materialize session from jwt
    const baseSession: Session = {
      user: { name: null, email: null, image: null },
      expires: new Date(Date.now() + 60_000).toISOString(),
    };
    const adapterUser: AdapterUser = {
      id: 'u1',
      email: 'a@example.com',
      emailVerified: null,
      name: 'a@example.com',
      image: null,
    };
    const session = await authOptions.callbacks!.session!({
      session: baseSession,
      token: token2 as JWT,
      user: adapterUser,
      newSession: undefined as unknown as never,
      trigger: 'update',
    });
    expect((session as { tenant?: string }).tenant).toBe('acme');
    expect((session as { isAdmin?: boolean }).isAdmin).toBe(true);
    expect((session as { canCreate?: boolean }).canCreate).toBe(true);
    expect((session as { canApprove?: boolean }).canApprove).toBe(true);
    expect((session as { isLearner?: boolean }).isLearner).toBe(true);

    // Switch to beta
    const token3 = await authOptions.callbacks!.jwt!({
      token: token2 as JWT,
      user: undefined as unknown as NextAuthUser,
      account: null,
      profile: undefined,
      trigger: 'update',
      session: { tenant: 'beta' } as unknown as Session,
    });
    const session2 = await authOptions.callbacks!.session!({
      session: baseSession,
      token: token3 as JWT,
      user: adapterUser,
      newSession: undefined as unknown as never,
      trigger: 'update',
    });
    expect((session2 as { tenant?: string }).tenant).toBe('beta');
    expect((session2 as { isAdmin?: boolean }).isAdmin).toBe(false);
    expect((session2 as { canCreate?: boolean }).canCreate).toBe(false);
    expect((session2 as { canApprove?: boolean }).canApprove).toBe(false);
    expect((session2 as { isLearner?: boolean }).isLearner).toBe(true);
  });
});
