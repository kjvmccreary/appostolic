/**
 * Roles flags used across the app. Mirrors API Roles enum names.
 */
export type FlagRole = 'TenantAdmin' | 'Approver' | 'Creator' | 'Learner';

/** Legacy membership role from API (pre-flags). */
export type LegacyRole = 'Owner' | 'Admin' | 'Editor' | 'Viewer';

export type Membership = {
  tenantId: string;
  tenantSlug: string;
  role: LegacyRole;
  /** Optional roles flags provided by API; when absent, derive from legacy role. */
  roles?: FlagRole[];
};

/**
 * Derive Roles flags from a legacy membership role.
 * Matches server-side RoleAuthorizationHandler mapping for compatibility.
 */
export function deriveFlagsFromLegacy(role: LegacyRole): FlagRole[] {
  switch (role) {
    case 'Owner':
    case 'Admin':
      return ['TenantAdmin', 'Approver', 'Creator', 'Learner'];
    case 'Editor':
      return ['Creator', 'Learner'];
    case 'Viewer':
    default:
      return ['Learner'];
  }
}

/**
 * Normalize a membership's roles as a set of flag role names.
 * If membership.roles is present, use it; otherwise derive from legacy role.
 */
export function getFlagRoles(m: Membership | null | undefined): FlagRole[] {
  if (!m) return [];
  if (m.roles && Array.isArray(m.roles) && m.roles.length) return dedupe(m.roles);
  return deriveFlagsFromLegacy(m.role);
}

/**
 * Compute convenience booleans for the selected tenant based on roles flags.
 */
export function computeBooleansForTenant(
  memberships: Membership[] | null | undefined,
  tenantSlug: string | null | undefined,
): {
  isAdmin: boolean;
  canApprove: boolean;
  canCreate: boolean;
  isLearner: boolean;
  roles: FlagRole[];
} {
  const list = memberships ?? [];
  const mem = tenantSlug ? (list.find((m) => m.tenantSlug === tenantSlug) ?? null) : null;
  const roles = getFlagRoles(mem);
  const has = (r: FlagRole) => roles.includes(r);
  return {
    isAdmin: has('TenantAdmin'),
    canApprove: has('TenantAdmin') || has('Approver'),
    canCreate: has('TenantAdmin') || has('Creator'),
    isLearner: has('Learner') || has('Creator') || has('Approver') || has('TenantAdmin'),
    roles,
  };
}

function dedupe<T extends string>(arr: T[]): T[] {
  return Array.from(new Set(arr));
}
