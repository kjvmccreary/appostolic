import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../src/lib/serverEnv';
import { buildProxyHeaders } from '../../../../src/lib/proxyHeaders';

export const runtime = 'nodejs';

// GET /api-proxy/users/me -> /api/users/me
export async function GET() {
  const headers = await buildProxyHeaders({ requireTenant: false });
  if (!headers) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const target = `${API_BASE}/api/users/me`;
  const res = await fetch(target, { headers });
  if (!res.ok) return NextResponse.json({ error: 'Upstream error' }, { status: res.status });
  const json = await res.json();
  return NextResponse.json(json, { status: 200 });
}

// PUT /api-proxy/users/me -> /api/users/me (merge patch)
export async function PUT(req: NextRequest) {
  const headers = await buildProxyHeaders({ requireTenant: false });
  if (!headers) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  headers['content-type'] = 'application/json';
  const body = await req.text();
  const target = `${API_BASE}/api/users/me`;
  const res = await fetch(target, { method: 'PUT', headers, body });
  if (!res.ok) return NextResponse.json(null, { status: res.status });
  const json = await res.json();
  return NextResponse.json(json, { status: 200 });
}
