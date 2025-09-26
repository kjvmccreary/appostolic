import { NextResponse } from 'next/server';
import { API_BASE } from '../../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../../src/lib/proxyHeaders';

export const runtime = 'nodejs';

// GET proxies denomination metadata without tenant requirement while preserving auth cookies.
export async function GET() {
  const proxyContext = await buildProxyHeaders({ requireTenant: false });
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(`${API_BASE}/api/metadata/denominations`, {
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
