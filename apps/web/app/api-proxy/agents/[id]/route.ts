import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../../src/lib/proxyHeaders';
import { requireCanCreate } from '../../../../src/lib/roleGuard';

export const runtime = 'nodejs';

// headers are built from session (when enabled) or DEV_* envs

export async function GET(_req: NextRequest, { params }: { params: { id: string } }) {
  const target = `${API_BASE}/api/agents/${encodeURIComponent(params.id)}`;
  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(target, { method: 'GET', headers: proxyContext.headers });
  const nextResponse = new NextResponse(res.body, {
    status: res.status,
    headers: new Headers({ 'content-type': res.headers.get('content-type') ?? 'application/json' }),
  });
  return applyProxyCookies(nextResponse, proxyContext);
}

export async function PUT(req: NextRequest, { params }: { params: { id: string } }) {
  const target = `${API_BASE}/api/agents/${encodeURIComponent(params.id)}`;
  const body = await req.text();
  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const guard = await requireCanCreate();
  if (guard) return guard;
  const res = await fetch(target, { method: 'PUT', headers: proxyContext.headers, body });
  const nextResponse = new NextResponse(res.body, {
    status: res.status,
    headers: new Headers({ 'content-type': res.headers.get('content-type') ?? 'application/json' }),
  });
  return applyProxyCookies(nextResponse, proxyContext);
}

export async function DELETE(_req: NextRequest, { params }: { params: { id: string } }) {
  const target = `${API_BASE}/api/agents/${encodeURIComponent(params.id)}`;
  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const guard = await requireCanCreate();
  if (guard) return guard;
  const res = await fetch(target, { method: 'DELETE', headers: proxyContext.headers });
  const nextResponse = new NextResponse(null, { status: res.status });
  return applyProxyCookies(nextResponse, proxyContext);
}
