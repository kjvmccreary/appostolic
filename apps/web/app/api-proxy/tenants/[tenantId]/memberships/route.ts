import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../../../src/lib/proxyHeaders';
import { requireTenantAdmin } from '../../../../../src/lib/roleGuard';

export const runtime = 'nodejs';

// List memberships with roles flags (proxy to API 2.1 list)
export async function GET(_req: NextRequest, { params }: { params: { tenantId: string } }) {
  const guard = await requireTenantAdmin({ id: params.tenantId });
  if (guard) return guard;
  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(`${API_BASE}/api/tenants/${params.tenantId}/memberships`, {
    method: 'GET',
    headers: proxyContext.headers,
    cache: 'no-store',
  });
  const body = await res.text();
  const nextResponse = new NextResponse(body, {
    status: res.status,
    headers: { 'Content-Type': res.headers.get('Content-Type') ?? 'application/json' },
  });
  return applyProxyCookies(nextResponse, proxyContext);
}
