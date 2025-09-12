import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../src/lib/serverEnv';
import { buildProxyHeaders } from '../../../src/lib/proxyHeaders';

export const runtime = 'nodejs';

async function proxyHeadersOr401() {
  const headers = await buildProxyHeaders();
  if (!headers) return null;
  return headers;
}

export async function GET(req: NextRequest) {
  const search = req.nextUrl.search || '';
  const target = `${API_BASE}/api/agent-tasks${search}`;
  const headers = await proxyHeadersOr401();
  if (!headers) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(target, {
    method: 'GET',
    headers,
    cache: 'no-store',
  });
  return new NextResponse(res.body, {
    status: res.status,
    headers: new Headers({ 'content-type': res.headers.get('content-type') ?? 'application/json' }),
  });
}

export async function POST(req: NextRequest) {
  const target = `${API_BASE}/api/agent-tasks`;
  const body = await req.text();
  const headers = await proxyHeadersOr401();
  if (!headers) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(target, {
    method: 'POST',
    headers,
    body,
  });
  const responseHeaders = new Headers({
    'content-type': res.headers.get('content-type') ?? 'application/json',
  });
  const location = res.headers.get('location');
  if (location) responseHeaders.set('location', location);
  return new NextResponse(res.body, { status: res.status, headers: responseHeaders });
}
