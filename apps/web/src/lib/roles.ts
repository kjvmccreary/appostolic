/**
 * Roles flags used across the app. Mirrors API Roles enum names.
 */
export type FlagRole = 'TenantAdmin' | 'Approver' | 'Creator' | 'Learner';

/** Legacy membership role from API (pre-flags). */
// Legacy role type retained only for typing incoming data; not used in logic anymore.
export type LegacyRole = 'Owner' | 'Admin' | 'Editor' | 'Viewer';

export type Membership = {
  tenantId: string;
  tenantSlug: string;
  role: LegacyRole; // ignored for authority; kept for display / backward visibility only
  roles?: Array<FlagRole | string>; // authoritative source of truth
};

/** Return roles flags (canonical) from membership.roles; ignore legacy role completely. */
export function getFlagRoles(m: Membership | null | undefined): FlagRole[] {
  if (!m) return [];
  if (!m.roles || !Array.isArray(m.roles)) return [];
  const acc: FlagRole[] = [];
  for (const raw of m.roles) {
    const lower = String(raw).trim().toLowerCase();
    switch (lower) {
      case 'tenantadmin':
        acc.push('TenantAdmin');
        break;
      case 'approver':
        acc.push('Approver');
        break;
      case 'creator':
        acc.push('Creator');
        break;
      case 'learner':
        acc.push('Learner');
        break;
      default:
        // Ignore legacy labels and unknown strings silently now.
        break;
    }
  }
  return dedupe(acc);
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

// Bit values must mirror API Roles enum ordering; adjust if server changes.
// Assuming (from server context): TenantAdmin=1, Approver=2, Creator=4, Learner=8.
const FLAG_ORDER: [FlagRole, number][] = [
  ['TenantAdmin', 1],
  ['Approver', 2],
  ['Creator', 4],
  ['Learner', 8],
];

/** Convert a numeric flags bitmask (API integer) to an ordered list of flag names. */
export function roleNamesFromFlags(value: number): FlagRole[] {
  if (!value) return [];
  const names: FlagRole[] = [];
  for (const [name, bit] of FLAG_ORDER) if ((value & bit) === bit) names.push(name);
  return names;
}
