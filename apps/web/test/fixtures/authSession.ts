import type { Session } from 'next-auth';
import { computeBooleansForTenant, type FlagRole } from '../../src/lib/roles';

export type SessionMembership = {
  tenantId: string;
  tenantSlug: string;
  role?: string;
  roles?: number | string | Array<FlagRole | string>;
};

export type SessionWithClaims = Session & {
  memberships?: SessionMembership[];
  tenant?: string;
  isAdmin?: boolean;
  canApprove?: boolean;
  canCreate?: boolean;
  isLearner?: boolean;
  rolesForTenant?: FlagRole[];
  // Allow consumers to tack on extra fields (e.g., isSuperAdmin)
  [extra: string]: unknown;
};

export type SessionFactoryOptions = {
  email?: string;
  name?: string | null;
  tenant?: string | null;
  memberships?: SessionMembership[];
  expiresInSeconds?: number;
  issuedAt?: number;
  extras?: Record<string, unknown>;
};

let membershipCounter = 0;

// Builds a membership record with sane defaults and incrementing tenant IDs for test isolation.
export function makeMembership(options: {
  tenantSlug: string;
  tenantId?: string;
  role?: string;
  roles?: Array<FlagRole | string> | number | string;
}): SessionMembership {
  const { tenantSlug, tenantId, role = 'Member', roles } = options;
  return {
    tenantSlug,
    tenantId: tenantId ?? `tenant-${++membershipCounter}`,
    role,
    ...(roles !== undefined ? { roles } : {}),
  };
}

// Central session factory aligning with NextAuth JWT payload layout for component tests.
export function makeSession(options?: SessionFactoryOptions): SessionWithClaims {
  const {
    email = 'user@example.com',
    name = null,
    tenant = undefined,
    memberships = [],
    expiresInSeconds = 60 * 60,
    issuedAt = Date.now(),
    extras,
  } = options ?? {};

  const expires = new Date(issuedAt + expiresInSeconds * 1000).toISOString();

  const rolesInput = memberships.map((m) => ({
    tenantId: m.tenantId,
    tenantSlug: m.tenantSlug,
    roles: m.roles,
  }));

  const selectedTenant = tenant ?? undefined;
  const { isAdmin, canApprove, canCreate, isLearner, roles } = computeBooleansForTenant(
    rolesInput,
    selectedTenant ?? null,
  );

  const session: SessionWithClaims = {
    user: { email, name: name ?? undefined },
    expires,
    memberships,
    ...(selectedTenant ? { tenant: selectedTenant } : {}),
    isAdmin,
    canApprove,
    canCreate,
    isLearner,
    rolesForTenant: roles,
  };

  if (extras) {
    Object.assign(session, extras);
  }

  return session;
}

// Produces a fully-hydrated session selecting no tenant (neutral scope) by default.
export function makeNeutralSession(
  options?: Omit<SessionFactoryOptions, 'tenant'>,
): SessionWithClaims {
  return makeSession({ ...options, tenant: null });
}

// Produces a tenant-selected session, defaulting to the first membership's slug when not specified.
export function makeTenantSession(
  options?: Omit<SessionFactoryOptions, 'tenant'> & { tenant?: string },
): SessionWithClaims {
  const memberships = options?.memberships ?? [];
  const defaultTenant = options?.tenant ?? memberships[0]?.tenantSlug;
  return makeSession({ ...options, tenant: defaultTenant ?? null, memberships });
}

// Returns an anonymous session shape mirroring NextAuth's unauthenticated state for guards.
export function makeUnauthenticatedSession(): SessionWithClaims {
  const expires = new Date(Date.now() + 60 * 60 * 1000).toISOString();
  return {
    user: undefined,
    expires,
    memberships: [],
    isAdmin: false,
    canApprove: false,
    canCreate: false,
    isLearner: false,
    rolesForTenant: [],
  };
}
