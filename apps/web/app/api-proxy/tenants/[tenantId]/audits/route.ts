import { NextRequest } from 'next/server';
import { API_BASE } from '../../../../../src/lib/serverEnv';
import { buildProxyHeaders } from '../../../../../src/lib/proxyHeaders';
import { requireTenantAdmin } from '../../../../../src/lib/roleGuard';

export const runtime = 'nodejs';

// List audits with optional filters and paging
export async function GET(req: NextRequest, { params }: { params: { tenantId: string } }) {
  const guard = await requireTenantAdmin({ id: params.tenantId });
  if (guard) return guard;
  const headers = await buildProxyHeaders();
  if (!headers) return new Response('Unauthorized', { status: 401 });

  const qs = req.nextUrl.searchParams.toString();
  const url = `${API_BASE}/api/tenants/${params.tenantId}/audits${qs ? `?${qs}` : ''}`;
  const res = await fetch(url, { method: 'GET', headers, cache: 'no-store' });
  const body = await res.text();
  const forwardHeaders: HeadersInit = {
    'Content-Type': res.headers.get('Content-Type') ?? 'application/json',
  };
  const total = res.headers.get('X-Total-Count');
  if (total) (forwardHeaders as Record<string, string>)['X-Total-Count'] = total;
  return new Response(body, { status: res.status, headers: forwardHeaders });
}
