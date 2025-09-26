import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../../src/lib/proxyHeaders';

export const runtime = 'nodejs';

// GET /api-proxy/users/me -> /api/users/me
export async function GET() {
  const proxyContext = await buildProxyHeaders({ requireTenant: false });
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const target = `${API_BASE}/api/users/me`;
  const res = await fetch(target, { headers: proxyContext.headers });
  if (!res.ok) {
    const errorResponse = NextResponse.json({ error: 'Upstream error' }, { status: res.status });
    return applyProxyCookies(errorResponse, proxyContext);
  }
  const json = await res.json();
  const success = NextResponse.json(json, { status: 200 });
  return applyProxyCookies(success, proxyContext);
}

// PUT /api-proxy/users/me -> /api/users/me (merge patch)
export async function PUT(req: NextRequest) {
  const proxyContext = await buildProxyHeaders({ requireTenant: false });
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const body = await req.text();
  const target = `${API_BASE}/api/users/me`;
  const headers = { ...proxyContext.headers, 'content-type': 'application/json' };
  const res = await fetch(target, { method: 'PUT', headers, body });
  if (!res.ok) {
    const errorResponse = NextResponse.json(null, { status: res.status });
    return applyProxyCookies(errorResponse, proxyContext);
  }
  const json = await res.json();
  const success = NextResponse.json(json, { status: 200 });
  return applyProxyCookies(success, proxyContext);
}
