import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../../../src/lib/proxyHeaders';

export const runtime = 'nodejs';

export async function GET(req: NextRequest, { params }: { params: { id: string } }) {
  const search = req.nextUrl.search || '';
  const target = `${API_BASE}/api/agent-tasks/${encodeURIComponent(params.id)}/export${search}`;
  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(target, {
    method: 'GET',
    headers: proxyContext.headers,
    cache: 'no-store',
  });

  // Forward content headers to support download filename/content-type
  const responseHeaders = new Headers();
  const contentType = res.headers.get('content-type');
  const contentDisposition = res.headers.get('content-disposition');
  if (contentType) responseHeaders.set('content-type', contentType);
  if (contentDisposition) responseHeaders.set('content-disposition', contentDisposition);
  const nextResponse = new NextResponse(res.body, { status: res.status, headers: responseHeaders });
  return applyProxyCookies(nextResponse, proxyContext);
}
