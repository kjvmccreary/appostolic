import { NextResponse } from 'next/server';
import { API_BASE } from '../../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../../src/lib/proxyHeaders';

export const runtime = 'nodejs';

export async function GET() {
  const target = `${API_BASE}/api/dev/agents`;
  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(target, { method: 'GET', headers: proxyContext.headers });
  const nextResponse = new NextResponse(res.body, {
    status: res.status,
    headers: new Headers({ 'content-type': res.headers.get('content-type') ?? 'application/json' }),
  });
  return applyProxyCookies(nextResponse, proxyContext);
}
