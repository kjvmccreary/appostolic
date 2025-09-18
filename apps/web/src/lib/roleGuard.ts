import { cookies } from 'next/headers';
import { getServerSession } from 'next-auth';
import type { Session } from 'next-auth';
import { authOptions } from './auth';
import { computeBooleansForTenant, type Membership as RolesMembership } from './roles';

// Minimal helper to extract memberships from session token; legacy role strings ignored.
function getMemberships(session: Session | null): RolesMembership[] {
  return ((session as unknown as { memberships?: RolesMembership[] } | null)?.memberships ??
    []) as RolesMembership[];
}

/** Flags-based guard for Creator/Approver/Admin. Legacy roles are no longer considered. */
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
    memberships,
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
