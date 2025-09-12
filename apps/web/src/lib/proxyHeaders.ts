import { getServerSession } from 'next-auth';
import type { Session } from 'next-auth';
import { authOptions } from './auth';
import { DEV_TENANT, DEV_USER } from './serverEnv';

const WEB_AUTH_ENABLED = (process.env.WEB_AUTH_ENABLED ?? 'false').toLowerCase() === 'true';

function getSessionTenant(session: Session | null): string | undefined {
  const s = session as unknown as { tenant?: string } | null;
  return s?.tenant;
}

export async function buildProxyHeaders() {
  if (WEB_AUTH_ENABLED) {
    const session = await getServerSession(authOptions);
    const email = session?.user?.email;
    if (!email) return null;
    const tenant = getSessionTenant(session) || process.env.DEFAULT_TENANT || 'kevin-personal';
    return {
      'x-dev-user': String(email),
      'x-tenant': String(tenant),
      'Content-Type': 'application/json',
    } as const;
  }
  return {
    'x-dev-user': DEV_USER,
    'x-tenant': DEV_TENANT,
    'Content-Type': 'application/json',
  } as const;
}
