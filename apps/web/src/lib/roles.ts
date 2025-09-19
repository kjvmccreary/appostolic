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
  const TRACE = (process.env.NEXT_PUBLIC_DEBUG_ROLE_TRACE ?? '').toLowerCase() === 'true';
  const trace = (...args: unknown[]) => {
    if (TRACE && typeof window !== 'undefined') {
      console.log('[roles][trace]', ...args);
    }
  };
  trace('input-membership', {
    tenantId: m.tenantId,
    tenantSlug: m.tenantSlug,
    role: m.role,
    rolesType: typeof rawRoles,
    rolesValue: rawRoles,
  });

  // Accept numeric bitmask (number) or numeric string directly.
  if (typeof rawRoles === 'number' && Number.isFinite(rawRoles)) {
    const names = roleNamesFromFlags(rawRoles);
    trace('numeric-bitmask→names', rawRoles, names);
    return names;
  }
  if (typeof rawRoles === 'string' && /^\d+$/.test(rawRoles.trim())) {
    const bit = Number(rawRoles.trim());
    const names = roleNamesFromFlags(bit);
    trace('string-numeric-bitmask→names', bit, names);
    return names;
  }

  // If roles[] missing or empty array, optionally fall back to legacy role mapping during transition.
  // (Important: a numeric 0 bitmask should yield empty roles, not fallback to legacy.)
  if (!rawRoles || (Array.isArray(rawRoles) && rawRoles.length === 0)) {
    // Accept a comma-separated string of role tokens (e.g., "TenantAdmin, Approver, Creator, Learner")
    if (typeof rawRoles === 'string' && rawRoles.includes(',')) {
      const parts = rawRoles
        .split(',')
        .map((p) => p.trim())
        .filter((p) => p.length > 0);
      const parsed: FlagRole[] = [];
      for (const p of parts) {
        const lower = p.toLowerCase();
        switch (lower) {
          case 'tenantadmin':
            parsed.push('TenantAdmin');
            break;
          case 'approver':
            parsed.push('Approver');
            break;
          case 'creator':
            parsed.push('Creator');
            break;
          case 'learner':
            parsed.push('Learner');
            break;
          case 'admin':
          case 'owner':
            parsed.push('TenantAdmin', 'Approver', 'Creator', 'Learner');
            break;
          case 'editor':
            parsed.push('Creator', 'Learner');
            break;
          case 'viewer':
            parsed.push('Learner');
            break;
          default:
            break; // ignore unknown token
        }
      }
      return dedupe(parsed);
    }
    if (!legacyFallbackEnabled) return [];
    switch (m.role) {
      case 'Owner':
      case 'Admin':
        trace('legacy-fallback-admin-like');
        return ['TenantAdmin', 'Approver', 'Creator', 'Learner'];
      case 'Editor':
        trace('legacy-fallback-editor');
        return ['Creator', 'Learner'];
      case 'Viewer':
      default:
        trace('legacy-fallback-viewer');
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
        trace('ignore-unknown-role-token', raw);
        break; // ignore unknown strings silently
    }
  }
  trace('deduped-return', acc);
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
