import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../src/lib/serverEnv';
import { buildProxyHeaders } from '../../../src/lib/proxyHeaders';
import { getServerSession } from 'next-auth';
import { authOptions } from '../../../src/lib/auth';

export const runtime = 'nodejs';

// headers are built from session (when enabled) or DEV_* envs

export async function GET(req: NextRequest) {
  const search = req.nextUrl.search || '';
  const target = `${API_BASE}/api/agents${search}`;
  const headers = await buildProxyHeaders({ requireTenant: true });
  if (!headers) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(target, { method: 'GET', headers });
  return new NextResponse(res.body, {
    status: res.status,
    headers: new Headers({ 'content-type': res.headers.get('content-type') ?? 'application/json' }),
  });
}

export async function POST(req: NextRequest) {
  const target = `${API_BASE}/api/agents`;
  const body = await req.text();
  const headers = await buildProxyHeaders();
  if (!headers) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  // Guard: require canCreate for creating agents
  const session = await getServerSession(authOptions).catch(() => null);
  const canCreate = Boolean((session as unknown as { canCreate?: boolean } | null)?.canCreate);
  if (!canCreate) return NextResponse.json({ error: 'Forbidden' }, { status: 403 });
  const res = await fetch(target, { method: 'POST', headers, body });
  const responseHeaders = new Headers({
    'content-type': res.headers.get('content-type') ?? 'application/json',
  });
  const location = res.headers.get('location');
  if (location) responseHeaders.set('location', location);
  return new NextResponse(res.body, { status: res.status, headers: responseHeaders });
}
