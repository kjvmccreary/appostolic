import { cookies } from 'next/headers';
import { getServerSession } from 'next-auth';
import type { Session } from 'next-auth';
import { authOptions } from './auth';

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
  if (!allowed.includes(mem.role)) throw new Error('Insufficient role');
  return mem;
}
