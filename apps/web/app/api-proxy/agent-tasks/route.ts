import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../src/lib/serverEnv';
import {
  applyProxyCookies,
  buildProxyHeaders,
  type ProxyHeadersContext,
} from '../../../src/lib/proxyHeaders';

export const runtime = 'nodejs';

async function proxyContextOr401(): Promise<ProxyHeadersContext | null> {
  const context = await buildProxyHeaders();
  if (!context) return null;
  return context;
}

export async function GET(req: NextRequest) {
  const search = req.nextUrl.search || '';
  const target = `${API_BASE}/api/agent-tasks${search}`;
  const proxyContext = await proxyContextOr401();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(target, {
    method: 'GET',
    headers: proxyContext.headers,
    cache: 'no-store',
  });
  const nextResponse = new NextResponse(res.body, {
    status: res.status,
    headers: new Headers({ 'content-type': res.headers.get('content-type') ?? 'application/json' }),
  });
  return applyProxyCookies(nextResponse, proxyContext);
}

export async function POST(req: NextRequest) {
  const target = `${API_BASE}/api/agent-tasks`;
  const body = await req.text();
  const proxyContext = await proxyContextOr401();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(target, {
    method: 'POST',
    headers: proxyContext.headers,
    body,
  });
  const responseHeaders = new Headers({
    'content-type': res.headers.get('content-type') ?? 'application/json',
  });
  const location = res.headers.get('location');
  if (location) responseHeaders.set('location', location);
  const nextResponse = new NextResponse(res.body, { status: res.status, headers: responseHeaders });
  return applyProxyCookies(nextResponse, proxyContext);
}
