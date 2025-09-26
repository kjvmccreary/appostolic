import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../../src/lib/proxyHeaders';

export const runtime = 'nodejs';

export async function POST(req: NextRequest) {
  const target = `${API_BASE}/api/auth/change-password`;
  const body = await req.text();
  const proxyContext = await buildProxyHeaders({ requireTenant: false });
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const headers = { ...proxyContext.headers, 'content-type': 'application/json' };
  const res = await fetch(target, { method: 'POST', headers, body });
  const nextResponse = new NextResponse(null, { status: res.status });
  return applyProxyCookies(nextResponse, proxyContext);
}
