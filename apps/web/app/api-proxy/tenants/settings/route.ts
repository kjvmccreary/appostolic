import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../../src/lib/proxyHeaders';
import { requireTenantAdmin } from '../../../../src/lib/roleGuard';

export const runtime = 'nodejs';

// GET proxies tenant settings from the API while enforcing the tenant admin guard and cookies.
export async function GET() {
  const guard = await requireTenantAdmin();
  if (guard) return guard;
  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(`${API_BASE}/api/tenants/settings`, {
    method: 'GET',
    headers: proxyContext.headers,
    cache: 'no-store',
  });
  const nextResponse = new NextResponse(res.body, {
    status: res.status,
    headers: new Headers({
      'content-type': res.headers.get('content-type') ?? 'application/json',
    }),
  });
  return applyProxyCookies(nextResponse, proxyContext);
}

// PUT forwards tenant settings updates to the API with admin verification and header bridging.
export async function PUT(req: NextRequest) {
  const guard = await requireTenantAdmin();
  if (guard) return guard;
  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const body = await req.text();
  const forwardHeaders = new Headers(proxyContext.headers);
  forwardHeaders.set('content-type', req.headers.get('content-type') ?? 'application/json');
  const res = await fetch(`${API_BASE}/api/tenants/settings`, {
    method: 'PUT',
    headers: forwardHeaders,
    body,
    cache: 'no-store',
  });
  const responseHeaders = new Headers({
    'content-type': res.headers.get('content-type') ?? 'application/json',
  });
  const location = res.headers.get('location');
  if (location) responseHeaders.set('location', location);
  const nextResponse = new NextResponse(res.body, {
    status: res.status,
    headers: responseHeaders,
  });
  return applyProxyCookies(nextResponse, proxyContext);
}
