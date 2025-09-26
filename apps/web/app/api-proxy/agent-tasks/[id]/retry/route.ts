import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../../../src/lib/proxyHeaders';

export const runtime = 'nodejs';

export async function POST(_req: NextRequest, { params }: { params: { id: string } }) {
  const target = `${API_BASE}/api/agent-tasks/${encodeURIComponent(params.id)}/retry`;
  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(target, { method: 'POST', headers: proxyContext.headers });
  const responseHeaders = new Headers({
    'content-type': res.headers.get('content-type') ?? 'application/json',
  });
  const location = res.headers.get('location');
  if (location) responseHeaders.set('location', location);
  const nextResponse = new NextResponse(res.body, { status: res.status, headers: responseHeaders });
  return applyProxyCookies(nextResponse, proxyContext);
}
