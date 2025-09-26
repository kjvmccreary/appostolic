import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../../src/lib/proxyHeaders';

export const runtime = 'nodejs';

export async function POST(req: NextRequest) {
  // Invitation acceptance allows user-only authorization; tenant may not be selected yet
  const proxyContext = await buildProxyHeaders({ requireTenant: false });
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const body = await req.text();
  const res = await fetch(`${API_BASE}/api/invites/accept`, {
    method: 'POST',
    headers: proxyContext.headers,
    body,
  });
  const text = await res.text();
  const nextResponse = new NextResponse(text, {
    status: res.status,
    headers: { 'Content-Type': res.headers.get('Content-Type') ?? 'application/json' },
  });
  return applyProxyCookies(nextResponse, proxyContext);
}
