import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../../src/lib/serverEnv';
import { buildProxyHeaders } from '../../../../../src/lib/proxyHeaders';

export const runtime = 'nodejs';

export async function POST(req: NextRequest) {
  // Proxy password change to backend user endpoint
  const target = `${API_BASE}/api/users/me/password`;
  const body = await req.text();
  const headers = await buildProxyHeaders({ requireTenant: false });
  if (!headers) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  headers['content-type'] = 'application/json';
  const res = await fetch(target, { method: 'POST', headers, body });
  if (res.status === 204) return new NextResponse(null, { status: 204 });
  // Pass through error status without exposing body (avoid leaking reasons like "weak")
  return new NextResponse(null, { status: res.status });
}
