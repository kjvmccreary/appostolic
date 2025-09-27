import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../src/lib/serverEnv';
import { applyProxyCookies, buildProxyHeaders } from '../../../../src/lib/proxyHeaders';
import { requireTenantAdmin } from '../../../../src/lib/roleGuard';

export const runtime = 'nodejs';

/**
 * Proxies tenant logo upload/remove requests to the API while enforcing tenant-admin guards
 * and propagating refreshed cookies back to the browser.
 */
export async function POST(req: NextRequest) {
  const guard = await requireTenantAdmin();
  if (guard) return guard;

  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) {
    return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  }

  const incomingForm = await req.formData();
  const upstreamForm = new FormData();
  incomingForm.forEach((value, key) => {
    if (typeof value === 'string') {
      upstreamForm.set(key, value);
    } else {
      upstreamForm.set(key, value, value.name);
    }
  });

  const headers = new Headers(proxyContext.headers);
  headers.delete('content-type');

  const res = await fetch(`${API_BASE}/api/tenants/logo`, {
    method: 'POST',
    headers,
    body: upstreamForm,
    cache: 'no-store',
  });

  const nextResponse = new NextResponse(res.body, {
    status: res.status,
    headers: new Headers({
      'content-type': res.headers.get('content-type') ?? 'application/json',
    }),
  });

  return applyProxyCookies(nextResponse, proxyContext);
}

export async function DELETE() {
  const guard = await requireTenantAdmin();
  if (guard) return guard;

  const proxyContext = await buildProxyHeaders();
  if (!proxyContext) {
    return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  }

  const res = await fetch(`${API_BASE}/api/tenants/logo`, {
    method: 'DELETE',
    headers: proxyContext.headers,
    cache: 'no-store',
  });

  const nextResponse = new NextResponse(res.body, {
    status: res.status,
    headers: new Headers({
      'content-type': res.headers.get('content-type') ?? 'application/json',
    }),
  });

  return applyProxyCookies(nextResponse, proxyContext);
}
