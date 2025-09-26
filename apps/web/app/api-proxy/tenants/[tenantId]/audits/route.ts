import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../../../src/lib/proxyHeaders';
import { requireTenantAdmin } from '../../../../../src/lib/roleGuard';

export const runtime = 'nodejs';

// List audits with optional filters and paging
export async function GET(req: NextRequest, { params }: { params: { tenantId: string } }) {
  const guard = await requireTenantAdmin({ id: params.tenantId });
  if (guard) return guard;
  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });

  const qs = req.nextUrl.searchParams.toString();
  const url = `${API_BASE}/api/tenants/${params.tenantId}/audits${qs ? `?${qs}` : ''}`;
  const res = await fetch(url, {
    method: 'GET',
    headers: proxyContext.headers,
    cache: 'no-store',
  });
  const body = await res.text();
  const forwardHeaders: HeadersInit = {
    'Content-Type': res.headers.get('Content-Type') ?? 'application/json',
  };
  const total = res.headers.get('X-Total-Count');
  if (total) (forwardHeaders as Record<string, string>)['X-Total-Count'] = total;
  const nextResponse = new NextResponse(body, { status: res.status, headers: forwardHeaders });
  return applyProxyCookies(nextResponse, proxyContext);
}
