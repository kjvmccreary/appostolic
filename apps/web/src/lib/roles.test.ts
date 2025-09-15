import {
  computeBooleansForTenant,
  deriveFlagsFromLegacy,
  getFlagRoles,
  type Membership,
} from './roles';

describe('roles helpers', () => {
  const base = (over: Partial<Membership>): Membership => ({
    tenantId: 't1',
    tenantSlug: 'acme',
    role: 'Viewer',
    ...over,
  });

  it('derives flags from legacy Owner/Admin/Editor/Viewer', () => {
    expect(deriveFlagsFromLegacy('Owner')).toEqual([
      'TenantAdmin',
      'Approver',
      'Creator',
      'Learner',
    ]);
    expect(deriveFlagsFromLegacy('Admin')).toEqual([
      'TenantAdmin',
      'Approver',
      'Creator',
      'Learner',
    ]);
    expect(deriveFlagsFromLegacy('Editor')).toEqual(['Creator', 'Learner']);
    expect(deriveFlagsFromLegacy('Viewer')).toEqual(['Learner']);
  });

  it('prefers provided roles flags over legacy', () => {
    const m = base({ role: 'Viewer', roles: ['Creator', 'Learner'] });
    expect(getFlagRoles(m)).toEqual(['Creator', 'Learner']);
  });

  it('computes booleans for selected tenant', () => {
    const memberships: Membership[] = [
      base({ role: 'Admin', tenantSlug: 'acme' }),
      base({ role: 'Viewer', tenantSlug: 'beta' }),
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
