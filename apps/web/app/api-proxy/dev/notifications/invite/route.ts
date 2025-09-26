import { NextResponse } from 'next/server';
import { API_BASE } from '../../../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../../../src/lib/proxyHeaders';

export const runtime = 'nodejs';

export async function POST(req: Request) {
  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const body = await req.text();
  const res = await fetch(`${API_BASE}/api/dev/notifications/invite`, {
    method: 'POST',
    headers: proxyContext.headers,
    body,
  });
  const nextResponse = new NextResponse(res.body, { status: res.status });
  return applyProxyCookies(nextResponse, proxyContext);
}
