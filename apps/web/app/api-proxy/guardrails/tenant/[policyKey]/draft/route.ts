import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../../../../src/lib/proxyHeaders';
import { requireTenantAdmin } from '../../../../../../src/lib/roleGuard';

export const runtime = 'nodejs';

export async function PUT(req: NextRequest, { params }: { params: { policyKey: string } }) {
  const guard = await requireTenantAdmin();
  if (guard) return guard;

  const proxyContext = await buildProxyHeaders({ requireTenant: true });
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });

  const policyKey = encodeURIComponent(params.policyKey ?? 'default');
  const target = `${API_BASE}/api/guardrails/admin/tenant/${policyKey}/draft`;
  const body = await req.text();

  const res = await fetch(target, {
    method: 'PUT',
    headers: proxyContext.headers,
    body,
  });

  const headers = new Headers({
    'content-type': res.headers.get('content-type') ?? 'application/json',
  });
  const response = new NextResponse(res.body, { status: res.status, headers });
  return applyProxyCookies(response, proxyContext);
}
