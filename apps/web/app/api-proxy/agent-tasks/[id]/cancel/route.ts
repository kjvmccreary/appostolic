import { NextRequest, NextResponse } from 'next/server';
import { API_BASE, DEV_TENANT, DEV_USER } from '../../../../../src/lib/serverEnv';

export const runtime = 'nodejs';

function proxyHeaders() {
  return {
    'x-dev-user': DEV_USER,
    'x-tenant': DEV_TENANT,
    'Content-Type': 'application/json',
  } as const;
}

export async function POST(_req: NextRequest, { params }: { params: { id: string } }) {
  const target = `${API_BASE}/api/agent-tasks/${encodeURIComponent(params.id)}/cancel`;
  const res = await fetch(target, { method: 'POST', headers: proxyHeaders() });
  const headers = new Headers({
    'content-type': res.headers.get('content-type') ?? 'application/json',
  });
  return new NextResponse(res.body, { status: res.status, headers });
}
