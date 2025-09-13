import { NextRequest } from 'next/server';
import { API_BASE } from '../../../../../src/lib/serverEnv';
import { buildProxyHeaders } from '../../../../../src/lib/proxyHeaders';

export const runtime = 'nodejs';

export async function GET(_req: NextRequest, { params }: { params: { tenantId: string } }) {
  const headers = await buildProxyHeaders();
  if (!headers) return new Response('Unauthorized', { status: 401 });
  const res = await fetch(`${API_BASE}/api/tenants/${params.tenantId}/invites`, {
    method: 'GET',
    headers,
    cache: 'no-store',
  });
  const body = await res.text();
  return new Response(body, {
    status: res.status,
    headers: { 'Content-Type': res.headers.get('Content-Type') ?? 'application/json' },
  });
}

export async function POST(req: NextRequest, { params }: { params: { tenantId: string } }) {
  const headers = await buildProxyHeaders();
  if (!headers) return new Response('Unauthorized', { status: 401 });
  const body = await req.text();
  const res = await fetch(`${API_BASE}/api/tenants/${params.tenantId}/invites`, {
    method: 'POST',
    headers,
    body,
  });
  const text = await res.text();
  return new Response(text, {
    status: res.status,
    headers: { 'Content-Type': res.headers.get('Content-Type') ?? 'application/json' },
  });
}
