import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../../src/lib/proxyHeaders';
import { requireTenantAdmin } from '../../../../src/lib/roleGuard';

export const runtime = 'nodejs';

export async function GET(req: NextRequest) {
  const guard = await requireTenantAdmin();
  if (guard) return guard;

  const search = req.nextUrl.search || '';
  const target = `${API_BASE}/api/guardrails/admin/tenant${search}`;
  const proxyContext = await buildProxyHeaders({ requireTenant: true });
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });

  const res = await fetch(target, { method: 'GET', headers: proxyContext.headers });
  const headers = new Headers({
    'content-type': res.headers.get('content-type') ?? 'application/json',
  });
  const response = new NextResponse(res.body, { status: res.status, headers });
  return applyProxyCookies(response, proxyContext);
}
