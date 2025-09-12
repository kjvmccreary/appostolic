import { NextResponse } from 'next/server';
import { API_BASE } from '../../../../src/lib/serverEnv';
import { buildProxyHeaders } from '../../../../src/lib/proxyHeaders';

export const runtime = 'nodejs';

export async function GET() {
  const target = `${API_BASE}/api/dev/agents`;
  const headers = await buildProxyHeaders();
  if (!headers) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(target, { method: 'GET', headers });
  return new NextResponse(res.body, {
    status: res.status,
    headers: new Headers({ 'content-type': res.headers.get('content-type') ?? 'application/json' }),
  });
}
