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
  /**
   * Authoritative source of truth. During transition may appear as:
   * - Array<FlagRole|string> (canonical form)
   * - number (bitmask sent directly from API serializer)
   * - string numeric (e.g., "1"), treated like bitmask
   */
  roles?: number | string | Array<FlagRole | string>;
};

/** Return roles flags (canonical) from membership.roles; ignore legacy role completely. */
export function getFlagRoles(m: Membership | null | undefined): FlagRole[] {
  if (!m) return [];
  const rawRoles = m.roles as unknown;
  const legacyFallbackEnabled =
    (process.env.NEXT_PUBLIC_LEGACY_ROLE_FALLBACK ?? 'true').toLowerCase() !== 'false';

  // Accept numeric bitmask (number) or numeric string directly.
  if (typeof rawRoles === 'number' && Number.isFinite(rawRoles)) {
    return roleNamesFromFlags(rawRoles);
  }
  if (typeof rawRoles === 'string' && /^\d+$/.test(rawRoles.trim())) {
    return roleNamesFromFlags(Number(rawRoles.trim()));
  }

  // If roles[] missing or empty array, optionally fall back to legacy role mapping during transition.
  // (Important: a numeric 0 bitmask should yield empty roles, not fallback to legacy.)
  if (!rawRoles || (Array.isArray(rawRoles) && rawRoles.length === 0)) {
    if (!legacyFallbackEnabled) return [];
    switch (m.role) {
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

  if (!Array.isArray(rawRoles)) return []; // Defensive: unknown shape

  const acc: FlagRole[] = [];
  for (const raw of rawRoles) {
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
      // Allow legacy names inside roles[] as a soft transition (e.g., ['Admin']).
      case 'admin':
      case 'owner':
        acc.push('TenantAdmin', 'Approver', 'Creator', 'Learner');
        break;
      case 'editor':
        acc.push('Creator', 'Learner');
        break;
      case 'viewer':
        acc.push('Learner');
        break;
      default:
        break; // ignore unknown strings silently
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
