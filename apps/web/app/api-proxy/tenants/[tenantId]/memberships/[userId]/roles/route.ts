import { NextRequest } from 'next/server';
import { API_BASE } from '../../../../../../../src/lib/serverEnv';
import { buildProxyHeaders } from '../../../../../../../src/lib/proxyHeaders';
import { guardProxyRole } from '../../../../../../../src/lib/roleGuard';

export const runtime = 'nodejs';

// Replace roles flags for a membership (proxy to API 2.1 set)
export async function POST(
  req: NextRequest,
  { params }: { params: { tenantId: string; userId: string } },
) {
  const guard = await guardProxyRole({ tenantId: params.tenantId, anyOf: ['Owner', 'Admin'] });
  if (guard) return guard;
  const headers = await buildProxyHeaders();
  if (!headers) return new Response('Unauthorized', { status: 401 });
  const body = await req.text();
  const res = await fetch(
    `${API_BASE}/api/tenants/${params.tenantId}/memberships/${params.userId}/roles`,
    {
      method: 'POST',
      headers,
      body,
    },
  );
  const text = await res.text();
  return new Response(text, {
    status: res.status,
    headers: { 'Content-Type': res.headers.get('Content-Type') ?? 'application/json' },
  });
}
