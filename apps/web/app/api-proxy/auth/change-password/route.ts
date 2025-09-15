import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../src/lib/serverEnv';
import { buildProxyHeaders } from '../../../../src/lib/proxyHeaders';

export const runtime = 'nodejs';

export async function POST(req: NextRequest) {
  const target = `${API_BASE}/api/auth/change-password`;
  const body = await req.text();
  const headers = await buildProxyHeaders({ requireTenant: false });
  if (!headers) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  headers['content-type'] = 'application/json';
  const res = await fetch(target, { method: 'POST', headers, body });
  return new NextResponse(null, { status: res.status });
}
