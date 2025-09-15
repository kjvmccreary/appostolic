import { cookies } from 'next/headers';
import { getServerSession } from 'next-auth';
import type { Session } from 'next-auth';
import { authOptions } from './auth';
import { computeBooleansForTenant, type Membership as RolesMembership } from './roles';

export type Role = 'Owner' | 'Admin' | 'Editor' | 'Viewer';

type Membership = { tenantId: string; tenantSlug: string; role: Role };

function getMemberships(session: Session | null): Membership[] {
  return ((session as unknown as { memberships?: Membership[] } | null)?.memberships ??
    []) as Membership[];
}

/**
 * Find current membership for a given tenantId or the selected tenant (by slug cookie) if tenantId is not provided.
 */
export function pickMembership(
  session: Session | null,
  opts: { tenantId?: string; tenantSlug?: string } = {},
): Membership | null {
  const memberships = getMemberships(session);
  if (opts.tenantId) return memberships.find((m) => m.tenantId === opts.tenantId) ?? null;
  if (opts.tenantSlug) return memberships.find((m) => m.tenantSlug === opts.tenantSlug) ?? null;
  const slugFromCookie = cookies().get('selected_tenant')?.value;
  if (slugFromCookie) return memberships.find((m) => m.tenantSlug === slugFromCookie) ?? null;
  return null;
}

/**
 * Guard a server route (API proxy) with role requirements.
 * Returns a Response (401/403) when not authorized, or null when authorized.
 */
export async function guardProxyRole(params: {
  tenantId: string;
  anyOf: Role[];
}): Promise<Response | null> {
  const session = await getServerSession(authOptions);
  const email = session?.user?.email;
  if (!email) return new Response('Unauthorized', { status: 401 });
  const mem = pickMembership(session, { tenantId: params.tenantId });
  if (!mem) return new Response('Forbidden', { status: 403 });
  // Backward-compat: accept legacy Owner/Admin checks, but prefer flags-based evaluation
  const slug = mem.tenantSlug;
  const memberships = getMemberships(session);
  const { isAdmin } = computeBooleansForTenant(memberships as unknown as RolesMembership[], slug);
  // If legacy check requests Owner/Admin, treat TenantAdmin flag as sufficient.
  const requiresOwnerOrAdmin = params.anyOf.includes('Owner') || params.anyOf.includes('Admin');
  if (requiresOwnerOrAdmin) {
    if (!isAdmin) return new Response('Forbidden', { status: 403 });
    return null;
  }
  // Fallback legacy role name match for other cases (Editor/Viewer) if ever used.
  if (!params.anyOf.includes(mem.role)) return new Response('Forbidden', { status: 403 });
  return null;
}

/** Server-side helper to assert access in pages; returns membership or throws redirects/errors upstream. */
export async function requirePageRole(
  allowed: Role[],
  where?: { tenantId?: string; tenantSlug?: string },
): Promise<Membership> {
  const session = await getServerSession(authOptions);
  if (!session?.user?.email) {
    // Callers should handle redirect('/login') themselves for clarity; we throw an error to signal misuse.
    throw new Error('Not authenticated');
  }
  const mem = pickMembership(session, where);
  if (!mem) throw new Error('No membership for selected tenant');
  // As with proxy guard, treat Owner/Admin requirements as TenantAdmin flag
  const requiresOwnerOrAdmin = allowed.includes('Owner') || allowed.includes('Admin');
  if (requiresOwnerOrAdmin) {
    const memberships = getMemberships(session);
    const { isAdmin } = computeBooleansForTenant(
      memberships as unknown as RolesMembership[],
      mem.tenantSlug,
    );
    if (!isAdmin) throw new Error('Insufficient role');
    return mem;
  }
  if (!allowed.includes(mem.role)) throw new Error('Insufficient role');
  return mem;
}

/** Convenience: flags-based guard for Creator/Approver/Admin via session booleans. */
export async function guardByFlags(params: {
  tenantIdOrSlug?: { id?: string; slug?: string };
  require:
    | 'TenantAdmin'
    | 'Approver'
    | 'Creator'
    | 'Learner'
    | 'canCreate'
    | 'canApprove'
    | 'isAdmin';
}): Promise<Response | null> {
  const session = await getServerSession(authOptions);
  const email = session?.user?.email;
  if (!email) return new Response('Unauthorized', { status: 401 });
  const memberships = getMemberships(session);
  // Resolve tenant slug precedence: explicit slug → id lookup → cookie
  let slug = params.tenantIdOrSlug?.slug ?? null;
  if (!slug && params.tenantIdOrSlug?.id) {
    const mem = memberships.find((m) => m.tenantId === params.tenantIdOrSlug?.id);
    if (!mem) return new Response('Forbidden', { status: 403 });
    slug = mem.tenantSlug;
  }
  if (!slug) slug = cookies().get('selected_tenant')?.value ?? null;
  if (!slug) return new Response('Forbidden', { status: 403 });
  const { isAdmin, canApprove, canCreate, isLearner, roles } = computeBooleansForTenant(
    memberships as unknown as RolesMembership[],
    slug,
  );
  switch (params.require) {
    case 'isAdmin':
    case 'TenantAdmin':
      return isAdmin ? null : new Response('Forbidden', { status: 403 });
    case 'canApprove':
    case 'Approver':
      return canApprove ? null : new Response('Forbidden', { status: 403 });
    case 'canCreate':
    case 'Creator':
      return canCreate ? null : new Response('Forbidden', { status: 403 });
    case 'Learner':
      return isLearner || roles.includes('Learner')
        ? null
        : new Response('Forbidden', { status: 403 });
  }
}

/** G2M ergonomic wrappers for common guards */
export async function requireTenantAdmin(tenantIdOrSlug?: { id?: string; slug?: string }) {
  return guardByFlags({ tenantIdOrSlug, require: 'isAdmin' });
}

export async function requireCanCreate(tenantIdOrSlug?: { id?: string; slug?: string }) {
  return guardByFlags({ tenantIdOrSlug, require: 'canCreate' });
}
