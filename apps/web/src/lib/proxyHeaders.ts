import { cookies } from 'next/headers';
import { getServerSession } from 'next-auth';
import type { Session } from 'next-auth';
import { authOptions } from './auth';
import { DEFAULT_TENANT, DEV_TENANT, DEV_USER } from './serverEnv';

const WEB_AUTH_ENABLED = (process.env.WEB_AUTH_ENABLED ?? 'false').toLowerCase() === 'true';
const TENANT_COOKIE = 'selected_tenant';

function getSessionTenant(session: Session | null): string | undefined {
  const s = session as unknown as { tenant?: string } | null;
  return s?.tenant;
}

export async function buildProxyHeaders() {
  if (WEB_AUTH_ENABLED) {
    const session = await getServerSession(authOptions);
    const email = session?.user?.email;
    if (!email) return null;
    const c = cookies();
    const cookieTenant = c.get(TENANT_COOKIE)?.value;
    const tenant = getSessionTenant(session) || cookieTenant || DEFAULT_TENANT;
    return {
      'x-dev-user': String(email),
      'x-tenant': String(tenant),
      'Content-Type': 'application/json',
    } as const;
  }
  return {
    'x-dev-user': String(DEV_USER ?? ''),
    'x-tenant': String(DEV_TENANT ?? ''),
    'Content-Type': 'application/json',
  } as const;
}
