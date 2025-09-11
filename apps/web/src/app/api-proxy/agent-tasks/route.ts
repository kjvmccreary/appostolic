import { NextRequest, NextResponse } from 'next/server';
import { API_BASE, DEV_TENANT, DEV_USER } from '@/lib/serverEnv';

export const runtime = 'nodejs';

function proxyHeaders() {
  return {
    'x-dev-user': DEV_USER,
    'x-tenant': DEV_TENANT,
    'Content-Type': 'application/json',
  } as const;
}

export async function GET(req: NextRequest) {
  const search = req.nextUrl.search || '';
  const target = `${API_BASE}/api/agent-tasks${search}`;
  const res = await fetch(target, {
    method: 'GET',
    headers: proxyHeaders(),
    cache: 'no-store',
  });
  return new NextResponse(res.body, {
    status: res.status,
    headers: new Headers({ 'content-type': res.headers.get('content-type') ?? 'application/json' }),
  });
}

export async function POST(req: NextRequest) {
  const target = `${API_BASE}/api/agent-tasks`;
  const body = await req.text();
  const res = await fetch(target, {
    method: 'POST',
    headers: proxyHeaders(),
    body,
  });
  // Preserve location header if present for Created responses
  const headers = new Headers({
    'content-type': res.headers.get('content-type') ?? 'application/json',
  });
  const location = res.headers.get('location');
  if (location) headers.set('location', location);
  return new NextResponse(res.body, { status: res.status, headers });
}
