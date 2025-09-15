import { NextRequest } from 'next/server';
import { API_BASE } from '../../../../../../src/lib/serverEnv';
import { buildProxyHeaders } from '../../../../../../src/lib/proxyHeaders';
import { requireTenantAdmin } from '../../../../../../src/lib/roleGuard';

export const runtime = 'nodejs';

// Update member role
export async function PUT(
  req: NextRequest,
  { params }: { params: { tenantId: string; userId: string } },
) {
  const guard = await requireTenantAdmin({ id: params.tenantId });
  if (guard) return guard;
  const headers = await buildProxyHeaders();
  if (!headers) return new Response('Unauthorized', { status: 401 });
  const body = await req.text();
  const res = await fetch(`${API_BASE}/api/tenants/${params.tenantId}/members/${params.userId}`, {
    method: 'PUT',
    headers,
    body,
  });
  // No content on success
  if (res.status === 204) return new Response(null, { status: 204 });
  const text = await res.text();
  return new Response(text, {
    status: res.status,
    headers: { 'Content-Type': res.headers.get('Content-Type') ?? 'application/json' },
  });
}

// Remove member
export async function DELETE(
  _req: NextRequest,
  { params }: { params: { tenantId: string; userId: string } },
) {
  const guard = await requireTenantAdmin({ id: params.tenantId });
  if (guard) return guard;
  const headers = await buildProxyHeaders();
  if (!headers) return new Response('Unauthorized', { status: 401 });
  const res = await fetch(`${API_BASE}/api/tenants/${params.tenantId}/members/${params.userId}`, {
    method: 'DELETE',
    headers,
  });
  return new Response(null, { status: res.status });
}
