import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../../../src/lib/proxyHeaders';
import { requireSuperAdmin } from '../../../../../src/lib/roleGuard';

export const runtime = 'nodejs';

export async function PUT(req: NextRequest, { params }: { params: { slug: string } }) {
  const guard = await requireSuperAdmin();
  if (guard) return guard;

  const proxyContext = await buildProxyHeaders({ requireTenant: false });
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });

  const targetSlug = encodeURIComponent(params.slug ?? 'default');
  const target = `${API_BASE}/api/guardrails/admin/super/system/${targetSlug}`;
  const body = await req.text();

  const res = await fetch(target, {
    method: 'PUT',
    headers: proxyContext.headers,
    cache: 'no-store',
    body,
  });

  const headers = new Headers({
    'content-type': res.headers.get('content-type') ?? 'application/json',
  });
  const response = new NextResponse(res.body, { status: res.status, headers });
  return applyProxyCookies(response, proxyContext);
}
