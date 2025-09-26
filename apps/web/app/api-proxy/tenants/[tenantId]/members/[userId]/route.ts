import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../../../../src/lib/proxyHeaders';
import { requireTenantAdmin } from '../../../../../../src/lib/roleGuard';

export const runtime = 'nodejs';

// Update member role
export async function PUT(
  req: NextRequest,
  { params }: { params: { tenantId: string; userId: string } },
) {
  const guard = await requireTenantAdmin({ id: params.tenantId });
  if (guard) return guard;
  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const body = await req.text();
  const res = await fetch(`${API_BASE}/api/tenants/${params.tenantId}/members/${params.userId}`, {
    method: 'PUT',
    headers: proxyContext.headers,
    body,
  });
  // No content on success
  if (res.status === 204) {
    const empty = new NextResponse(null, { status: 204 });
    return applyProxyCookies(empty, proxyContext);
  }
  const text = await res.text();
  const nextResponse = new NextResponse(text, {
    status: res.status,
    headers: { 'Content-Type': res.headers.get('Content-Type') ?? 'application/json' },
  });
  return applyProxyCookies(nextResponse, proxyContext);
}

// Remove member
export async function DELETE(
  _req: NextRequest,
  { params }: { params: { tenantId: string; userId: string } },
) {
  const guard = await requireTenantAdmin({ id: params.tenantId });
  if (guard) return guard;
  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(`${API_BASE}/api/tenants/${params.tenantId}/members/${params.userId}`, {
    method: 'DELETE',
    headers: proxyContext.headers,
  });
  const nextResponse = new NextResponse(null, { status: res.status });
  return applyProxyCookies(nextResponse, proxyContext);
}
