/**
 * Roles flags used across the app. Mirrors API Roles enum names.
 */
export type FlagRole = 'TenantAdmin' | 'Approver' | 'Creator' | 'Learner';

// Deprecated legacy role property has been removed from the canonical Membership shape.
// If older code still references membership.role it will now be a type error.
export type Membership = {
  tenantId: string;
  tenantSlug: string;
  /**
   * Authoritative source of truth for tenant-scoped capabilities.
   * Supported representations:
   * - Array<FlagRole|string>
   * - number (bitmask)
   * - numeric string (coerced to bitmask)
   */
  roles?: number | string | Array<FlagRole | string>;
};

/** Return roles flags (canonical) from membership.roles; ignore legacy role completely. */
export function getFlagRoles(m: Membership | null | undefined): FlagRole[] {
  if (!m) return [];
  const rawRoles = m.roles as unknown;
  const TRACE = (process.env.NEXT_PUBLIC_DEBUG_ROLE_TRACE ?? '').toLowerCase() === 'true';
  const trace = (...args: unknown[]) => {
    if (TRACE && typeof window !== 'undefined') console.log('[roles][trace]', ...args);
  };

  // Numeric or numeric-string bitmask
  if (typeof rawRoles === 'number' && Number.isFinite(rawRoles)) {
    return roleNamesFromFlags(rawRoles);
  }
  if (typeof rawRoles === 'string') {
    const trimmed = rawRoles.trim();
    if (/^\d+$/.test(trimmed)) return roleNamesFromFlags(Number(trimmed));
    // Comma-separated canonical flag names supported for resilience (future removal possible)
    if (trimmed.includes(',')) {
      const acc: FlagRole[] = [];
      for (const token of trimmed
        .split(',')
        .map((p) => p.trim())
        .filter(Boolean)) {
        const lower = token.toLowerCase();
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
            trace('ignore-unknown-token', token);
            break;
        }
      }
      return dedupe(acc);
    }
    return []; // non-numeric simple string unsupported now
  }

  if (!Array.isArray(rawRoles)) return []; // Unknown shape
  const acc: FlagRole[] = [];
  for (const r of rawRoles) {
    const lower = String(r).trim().toLowerCase();
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
        trace('ignore-unknown-role-token', r);
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
