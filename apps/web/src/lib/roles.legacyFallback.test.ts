import { describe, it, expect } from 'vitest';
import { getFlagRoles, computeBooleansForTenant, type Membership } from './roles';

// These tests assert transitional legacy role fallback behavior when roles[] flags are absent.
// They can be removed once all API memberships populate explicit roles flags and fallback is disabled.

const base = (over: Partial<Membership>): Membership => ({
  tenantId: 't1',
  tenantSlug: 'acme',
  role: 'Viewer',
  ...over,
});

describe('roles legacy fallback (transitional)', () => {
  it('maps legacy Admin to full flag set when roles[] missing', () => {
    const m = base({ role: 'Admin', roles: undefined });
    expect(getFlagRoles(m)).toEqual(['TenantAdmin', 'Approver', 'Creator', 'Learner']);
  });

  it('maps legacy Owner equivalently to Admin', () => {
    const m = base({ role: 'Owner', roles: [] });
    expect(getFlagRoles(m)).toEqual(['TenantAdmin', 'Approver', 'Creator', 'Learner']);
  });

  it('maps legacy Editor to Creator + Learner', () => {
    const m = base({ role: 'Editor', roles: [] });
    expect(getFlagRoles(m)).toEqual(['Creator', 'Learner']);
  });

  it('maps legacy Viewer to Learner', () => {
    const m = base({ role: 'Viewer', roles: [] });
    expect(getFlagRoles(m)).toEqual(['Learner']);
  });

  it('computeBooleansForTenant reflects admin fallback', () => {
    const memberships: Membership[] = [base({ role: 'Admin', roles: [] })];
    const b = computeBooleansForTenant(memberships, 'acme');
    expect(b.isAdmin).toBe(true);
    expect(b.canCreate).toBe(true);
    expect(b.canApprove).toBe(true);
    expect(b.isLearner).toBe(true);
  });
});
