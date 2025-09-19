import { getFlagRoles, computeBooleansForTenant, type Membership } from './roles';

describe('roles numeric bitmask support', () => {
  function make(mem: Partial<Membership>): Membership {
    return {
      tenantId: 't1',
      tenantSlug: 'acme',
      role: 'Viewer',
      ...mem,
    } as Membership;
  }

  it('maps numeric bitmask 1 (TenantAdmin) to full admin roles when numeric provided', () => {
    const m = make({ roles: 1 });
    expect(getFlagRoles(m)).toEqual(['TenantAdmin']);
    const { isAdmin } = computeBooleansForTenant([m], 'acme');
    expect(isAdmin).toBe(true);
  });

  it('maps numeric bitmask 15 (all flags) to ordered roles', () => {
    const m = make({ roles: 15 });
    expect(getFlagRoles(m)).toEqual(['TenantAdmin', 'Approver', 'Creator', 'Learner']);
  });

  it('treats string numeric bitmask "7" as flags (TenantAdmin+Approver+Creator)', () => {
    const m = make({ roles: '7' });
    expect(getFlagRoles(m)).toEqual(['TenantAdmin', 'Approver', 'Creator']);
  });

  it('does not fall back to legacy when numeric bitmask is 0 (empty roles)', () => {
    const m = make({ role: 'Admin', roles: 0 });
    expect(getFlagRoles(m)).toEqual([]); // Explicit empty, no fallback
  });

  it('parses comma-separated roles string into canonical flags', () => {
    const m = make({ roles: 'TenantAdmin, Approver, Creator, Learner' });
    expect(getFlagRoles(m)).toEqual(['TenantAdmin', 'Approver', 'Creator', 'Learner']);
    const { isAdmin, canApprove, canCreate, isLearner } = computeBooleansForTenant([m], 'acme');
    expect(isAdmin).toBe(true);
    expect(canApprove).toBe(true);
    expect(canCreate).toBe(true);
    expect(isLearner).toBe(true);
  });
});
