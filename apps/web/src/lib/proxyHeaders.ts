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
  if (WEB_AUTH_ENABLED) {
    const session = await getServerSession(authOptions);
    const email = session?.user?.email;
    if (!email) return null;
    const hdrs: ProxyHeaders = {
      'x-dev-user': String(email),
      'Content-Type': 'application/json',
    };
    const c = cookies();
    const cookieTenant = c.get(TENANT_COOKIE)?.value;
    const sessionTenant = getSessionTenant(session);
    const tenant = sessionTenant || cookieTenant;
    if (tenant) {
      hdrs['x-tenant'] = String(tenant);
    } else if (requireTenant) {
      // No tenant selected; caller expects strict 401 handling upstream
      return null;
    } else {
      // permissive mode: omit x-tenant (API path may allow user-only auth)
    }
    return hdrs;
  }
  // Dev mode: rely on envs validated by serverEnv
  return {
    'x-dev-user': String(DEV_USER ?? ''),
    'x-tenant': String(DEV_TENANT ?? ''),
    'Content-Type': 'application/json',
  };
}
