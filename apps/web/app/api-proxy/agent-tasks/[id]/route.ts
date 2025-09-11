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

export async function GET(req: NextRequest, { params }: { params: { id: string } }) {
  const search = req.nextUrl.search || '';
  const target = `${API_BASE}/api/agent-tasks/${encodeURIComponent(params.id)}${search}`;
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
