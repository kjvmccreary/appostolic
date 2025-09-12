import { NextRequest, NextResponse } from 'next/server';
import { API_BASE, DEV_TENANT, DEV_USER } from '../../../../src/lib/serverEnv';

export const runtime = 'nodejs';

function proxyHeaders() {
  return {
    'x-dev-user': DEV_USER,
    'x-tenant': DEV_TENANT,
    'Content-Type': 'application/json',
  } as const;
}

export async function GET(_req: NextRequest, { params }: { params: { id: string } }) {
  const target = `${API_BASE}/api/agents/${encodeURIComponent(params.id)}`;
  const res = await fetch(target, { method: 'GET', headers: proxyHeaders(), cache: 'no-store' });
  return new NextResponse(res.body, {
    status: res.status,
    headers: new Headers({ 'content-type': res.headers.get('content-type') ?? 'application/json' }),
  });
}

export async function PUT(req: NextRequest, { params }: { params: { id: string } }) {
  const target = `${API_BASE}/api/agents/${encodeURIComponent(params.id)}`;
  const body = await req.text();
  const res = await fetch(target, { method: 'PUT', headers: proxyHeaders(), body });
  return new NextResponse(res.body, {
    status: res.status,
    headers: new Headers({ 'content-type': res.headers.get('content-type') ?? 'application/json' }),
  });
}

export async function DELETE(_req: NextRequest, { params }: { params: { id: string } }) {
  const target = `${API_BASE}/api/agents/${encodeURIComponent(params.id)}`;
  const res = await fetch(target, { method: 'DELETE', headers: proxyHeaders() });
  return new NextResponse(null, { status: res.status });
}
