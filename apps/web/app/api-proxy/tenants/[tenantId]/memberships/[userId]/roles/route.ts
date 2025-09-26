import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../../../../../src/lib/proxyHeaders';
import { requireTenantAdmin } from '../../../../../../../src/lib/roleGuard';

export const runtime = 'nodejs';

// Replace roles flags for a membership (proxy to API 2.1 set)
export async function POST(
  req: NextRequest,
  { params }: { params: { tenantId: string; userId: string } },
) {
  const guard = await requireTenantAdmin({ id: params.tenantId });
  if (guard) return guard;
  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const body = await req.text();
  const res = await fetch(
    `${API_BASE}/api/tenants/${params.tenantId}/memberships/${params.userId}/roles`,
    {
      method: 'POST',
      headers: proxyContext.headers,
      body,
    },
  );
  const text = await res.text();
  const nextResponse = new NextResponse(text, {
    status: res.status,
    headers: { 'Content-Type': res.headers.get('Content-Type') ?? 'application/json' },
  });
  return applyProxyCookies(nextResponse, proxyContext);
}
