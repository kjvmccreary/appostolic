import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../../src/lib/proxyHeaders';
import { requireTenantAdmin } from '../../../../src/lib/roleGuard';

export const runtime = 'nodejs';

async function ensureOwnerOrAdmin(): Promise<Response | null> {
  return requireTenantAdmin();
}

export async function GET(req: NextRequest) {
  const guard = await ensureOwnerOrAdmin();
  if (guard) return guard;

  const search = req.nextUrl.search || '';
  const target = `${API_BASE}/api/notifications/dlq${search}`;
  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(target, { method: 'GET', headers: proxyContext.headers });
  const responseHeaders = new Headers({
    'content-type': res.headers.get('content-type') ?? 'application/json',
  });
  const total = res.headers.get('x-total-count');
  if (total) responseHeaders.set('x-total-count', total);
  const nextResponse = new NextResponse(res.body, {
    status: res.status,
    headers: responseHeaders,
  });
  return applyProxyCookies(nextResponse, proxyContext);
}

export async function POST(req: NextRequest) {
  const guard = await ensureOwnerOrAdmin();
  if (guard) return guard;

  const target = `${API_BASE}/api/notifications/dlq/replay`;
  const body = await req.text();
  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(target, { method: 'POST', headers: proxyContext.headers, body });
  const nextResponse = new NextResponse(res.body, {
    status: res.status,
    headers: new Headers({ 'content-type': res.headers.get('content-type') ?? 'application/json' }),
  });
  return applyProxyCookies(nextResponse, proxyContext);
}
