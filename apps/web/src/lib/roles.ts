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
  /** Optional roles flags provided by API; when absent, derive from legacy role.
   * Accepts canonical FlagRole names or legacy labels (Admin/Owner/Editor/Viewer) which are normalized.
   */
  roles?: Array<FlagRole | string>;
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
  // If roles[] is provided, prefer it — but normalize common legacy/synonym names.
  if (m.roles && Array.isArray(m.roles) && m.roles.length) {
    const acc: FlagRole[] = [];
    for (const raw of m.roles) {
      const name = String(raw).trim();
      const lower = name.toLowerCase();
      switch (lower) {
        // Canonical flag names
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
        // Legacy role names seen in older payloads — expand to flag sets
        case 'admin':
          acc.push(...deriveFlagsFromLegacy('Admin'));
          break;
        case 'owner':
          acc.push(...deriveFlagsFromLegacy('Owner'));
          break;
        case 'editor':
          acc.push(...deriveFlagsFromLegacy('Editor'));
          break;
        case 'viewer':
          acc.push(...deriveFlagsFromLegacy('Viewer'));
          break;
        default:
          // Unknown label — ignore to avoid introducing accidental flags.
          break;
      }
    }
    const normalized = dedupe(acc);
    if (normalized.length) return normalized;
    // If normalization produced nothing (e.g., unexpected labels), fall back to legacy role.
    // Be tolerant of case: accept lowercase legacy role strings as well.
    const lower = String(m.role).trim().toLowerCase();
    switch (lower) {
      case 'owner':
        return deriveFlagsFromLegacy('Owner');
      case 'admin':
        return deriveFlagsFromLegacy('Admin');
      case 'editor':
        return deriveFlagsFromLegacy('Editor');
      case 'viewer':
        return deriveFlagsFromLegacy('Viewer');
      default:
        return deriveFlagsFromLegacy(m.role);
    }
  }
  // No roles[] provided — derive from legacy role.
  // Be tolerant of case: accept lowercase legacy role strings as well.
  const lower = String(m.role).trim().toLowerCase();
  switch (lower) {
    case 'owner':
      return deriveFlagsFromLegacy('Owner');
    case 'admin':
      return deriveFlagsFromLegacy('Admin');
    case 'editor':
      return deriveFlagsFromLegacy('Editor');
    case 'viewer':
      return deriveFlagsFromLegacy('Viewer');
    default:
      return deriveFlagsFromLegacy(m.role);
  }
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
