import { computeBooleansForTenant, getFlagRoles, type Membership } from './roles';

describe('roles helpers', () => {
  const base = (over: Partial<Membership>): Membership => ({
    tenantId: 't1',
    tenantSlug: 'acme',
    role: 'Viewer',
    ...over,
  });

  it('returns roles flags unchanged', () => {
    const m = base({ roles: ['Creator', 'Learner'] });
    expect(getFlagRoles(m)).toEqual(['Creator', 'Learner']);
  });

  it('computes booleans from flags (admin vs learner)', () => {
    const memberships: Membership[] = [
      base({ tenantSlug: 'acme', roles: ['TenantAdmin', 'Creator', 'Approver', 'Learner'] }),
      base({ tenantSlug: 'beta', roles: ['Learner'] }),
    ];
    const acme = computeBooleansForTenant(memberships, 'acme');
    expect(acme.isAdmin).toBe(true);
    expect(acme.canApprove).toBe(true);
    expect(acme.canCreate).toBe(true);
    expect(acme.isLearner).toBe(true);
    const beta = computeBooleansForTenant(memberships, 'beta');
    expect(beta.isAdmin).toBe(false);
    expect(beta.canApprove).toBe(false);
    expect(beta.canCreate).toBe(false);
    expect(beta.isLearner).toBe(true);
  });
});
