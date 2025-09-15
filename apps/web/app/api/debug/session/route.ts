import { NextResponse } from 'next/server';
import { getServerSession } from 'next-auth';
import { cookies } from 'next/headers';
import { authOptions } from '../../../../src/lib/auth';
import { buildProxyHeaders } from '../../../../src/lib/proxyHeaders';
import {
  computeBooleansForTenant,
  type Membership as RolesMembership,
} from '../../../../src/lib/roles';

export const runtime = 'nodejs';

export async function GET() {
  const session = await getServerSession(authOptions).catch(() => null);
  const email = session?.user?.email ?? null;
  const cookieTenant = cookies().get('selected_tenant')?.value ?? null;
  const hdrsLoose = await buildProxyHeaders({ requireTenant: false });
  const hdrsStrict = await buildProxyHeaders({ requireTenant: true });
  const memberships =
    (session as unknown as { memberships?: RolesMembership[] } | null)?.memberships ?? [];
  const selectedTenant = (session as unknown as { tenant?: string } | null)?.tenant ?? cookieTenant;
  const derived = computeBooleansForTenant(memberships, selectedTenant);
  return NextResponse.json({
    session: { email, raw: session, derived },
    cookies: { selected_tenant: cookieTenant },
    headersLoose: hdrsLoose,
    headersStrict: hdrsStrict,
  });
}
