import { cookies } from 'next/headers';
import { getServerSession } from 'next-auth';
import type { Session } from 'next-auth';
import { authOptions } from './auth';
import { DEV_TENANT, DEV_USER } from './serverEnv';

const WEB_AUTH_ENABLED = (process.env.WEB_AUTH_ENABLED ?? 'false').toLowerCase() === 'true';
const TENANT_COOKIE = 'selected_tenant';

function getSessionTenant(session: Session | null): string | undefined {
  const s = session as unknown as { tenant?: string } | null;
  return s?.tenant;
}

export type ProxyHeaders = Record<string, string>;

/**
 * Build headers for proxying to the API.
 * - When WEB_AUTH_ENABLED=true, requires a signed-in session (email) always.
 * - By default also requires a selected tenant (session.tenant or cookie), returning null if missing.
 * - For special endpoints that allow user-only auth (e.g., invite acceptance),
 *   pass { requireTenant: false } to proceed without x-tenant when not selected yet.
 * - When WEB_AUTH_ENABLED=false (pure dev mode), uses DEV_USER/DEV_TENANT envs.
 */
export async function buildProxyHeaders(options?: {
  requireTenant?: boolean;
}): Promise<ProxyHeaders | null> {
  const requireTenant = options?.requireTenant ?? true;

  // Always prefer an authenticated web session if present, regardless of WEB_AUTH_ENABLED flag.
  // This makes magic-link flows work in dev without toggling envs.
  const session = await getServerSession(authOptions).catch(() => null);
  const emailFromSession = session?.user?.email;
  if (emailFromSession) {
    const hdrs: ProxyHeaders = {
      'x-dev-user': String(emailFromSession),
      'Content-Type': 'application/json',
    };
    const c = cookies();
    const cookieTenant = c.get(TENANT_COOKIE)?.value;
    const sessionTenant = getSessionTenant(session as Session);
    const tenant = sessionTenant || cookieTenant;
    if (tenant) {
      hdrs['x-tenant'] = String(tenant);
    } else if (requireTenant) {
      return null;
    }
    return hdrs;
  }

  // If no session, fall back based on mode
  if (WEB_AUTH_ENABLED) {
    // Auth required but no session → unauthorized
    return null;
  }

  // Dev mode fallback: rely on envs validated by serverEnv
  return {
    'x-dev-user': String(DEV_USER ?? ''),
    'x-tenant': String(DEV_TENANT ?? ''),
    'Content-Type': 'application/json',
  };
}
