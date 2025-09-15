import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../src/lib/serverEnv';
import { buildProxyHeaders } from '../../../../src/lib/proxyHeaders';
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
  const headers = await buildProxyHeaders();
  if (!headers) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(target, { method: 'GET', headers });
  const responseHeaders = new Headers({
    'content-type': res.headers.get('content-type') ?? 'application/json',
  });
  const total = res.headers.get('x-total-count');
  if (total) responseHeaders.set('x-total-count', total);
  return new NextResponse(res.body, { status: res.status, headers: responseHeaders });
}

export async function POST(req: NextRequest) {
  const guard = await ensureOwnerOrAdmin();
  if (guard) return guard;

  const target = `${API_BASE}/api/notifications/dlq/replay`;
  const body = await req.text();
  const headers = await buildProxyHeaders();
  if (!headers) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(target, { method: 'POST', headers, body });
  return new NextResponse(res.body, {
    status: res.status,
    headers: new Headers({ 'content-type': res.headers.get('content-type') ?? 'application/json' }),
  });
}
