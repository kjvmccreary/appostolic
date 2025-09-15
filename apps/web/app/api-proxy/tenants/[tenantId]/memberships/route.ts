import { NextRequest } from 'next/server';
import { API_BASE } from '../../../../../src/lib/serverEnv';
import { buildProxyHeaders } from '../../../../../src/lib/proxyHeaders';
import { guardProxyRole } from '../../../../../src/lib/roleGuard';

export const runtime = 'nodejs';

// List memberships with roles flags (proxy to API 2.1 list)
export async function GET(_req: NextRequest, { params }: { params: { tenantId: string } }) {
  const guard = await guardProxyRole({ tenantId: params.tenantId, anyOf: ['Owner', 'Admin'] });
  if (guard) return guard;
  const headers = await buildProxyHeaders();
  if (!headers) return new Response('Unauthorized', { status: 401 });
  const res = await fetch(`${API_BASE}/api/tenants/${params.tenantId}/memberships`, {
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
