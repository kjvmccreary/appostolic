import { NextResponse } from 'next/server';
import { API_BASE } from '../../../../../src/lib/serverEnv';
import { buildProxyHeaders } from '../../../../../src/lib/proxyHeaders';

export const runtime = 'nodejs';

export async function POST(req: Request) {
  const headers = await buildProxyHeaders();
  if (!headers) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const body = await req.text();
  const res = await fetch(`${API_BASE}/api/dev/notifications/invite`, {
    method: 'POST',
    headers,
    body,
  });
  return new NextResponse(res.body, { status: res.status });
}
