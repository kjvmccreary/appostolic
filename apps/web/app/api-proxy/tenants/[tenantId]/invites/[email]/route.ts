import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../../../../src/lib/proxyHeaders';
import { requireTenantAdmin } from '../../../../../../src/lib/roleGuard';

export const runtime = 'nodejs';

// Resend
export async function POST(
  _req: NextRequest,
  { params }: { params: { tenantId: string; email: string } },
) {
  const guard = await requireTenantAdmin({ id: params.tenantId });
  if (guard) return guard;
  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(
    `${API_BASE}/api/tenants/${params.tenantId}/invites/${encodeURIComponent(params.email)}/resend`,
    {
      method: 'POST',
      headers: proxyContext.headers,
    },
  );
  const body = await res.text();
  const nextResponse = new NextResponse(body, {
    status: res.status,
    headers: { 'Content-Type': res.headers.get('Content-Type') ?? 'application/json' },
  });
  return applyProxyCookies(nextResponse, proxyContext);
}

// Revoke
export async function DELETE(
  _req: NextRequest,
  { params }: { params: { tenantId: string; email: string } },
) {
  const guard = await requireTenantAdmin({ id: params.tenantId });
  if (guard) return guard;
  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  const res = await fetch(
    `${API_BASE}/api/tenants/${params.tenantId}/invites/${encodeURIComponent(params.email)}`,
    {
      method: 'DELETE',
      headers: proxyContext.headers,
    },
  );
  const nextResponse = new NextResponse(null, { status: res.status });
  return applyProxyCookies(nextResponse, proxyContext);
}
