import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../src/lib/proxyHeaders';
import { requireCanCreate } from '../../../src/lib/roleGuard';

export const runtime = 'nodejs';

// headers are built from session (when enabled) or DEV_* envs

export async function GET(req: NextRequest) {
  const search = req.nextUrl.search || '';
  const target = `${API_BASE}/api/agents${search}`;
  const proxyContext = await buildProxyHeaders({ requireTenant: true });
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(target, { method: 'GET', headers: proxyContext.headers });
  const nextResponse = new NextResponse(res.body, {
    status: res.status,
    headers: new Headers({ 'content-type': res.headers.get('content-type') ?? 'application/json' }),
  });
  return applyProxyCookies(nextResponse, proxyContext);
}

export async function POST(req: NextRequest) {
  // Guard: require canCreate for creating agents (flags-based)
  const guard = await requireCanCreate();
  if (guard) return guard;
  const target = `${API_BASE}/api/agents`;
  const body = await req.text();
  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(target, { method: 'POST', headers: proxyContext.headers, body });
  const responseHeaders = new Headers({
    'content-type': res.headers.get('content-type') ?? 'application/json',
  });
  const location = res.headers.get('location');
  if (location) responseHeaders.set('location', location);
  const nextResponse = new NextResponse(res.body, { status: res.status, headers: responseHeaders });
  return applyProxyCookies(nextResponse, proxyContext);
}
