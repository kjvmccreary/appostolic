import { NextRequest } from 'next/server';
import { API_BASE } from '../../../../src/lib/serverEnv';
import { buildProxyHeaders } from '../../../../src/lib/proxyHeaders';

export const runtime = 'nodejs';

export async function POST(req: NextRequest) {
  // Invitation acceptance allows user-only authorization; tenant may not be selected yet
  const headers = await buildProxyHeaders({ requireTenant: false });
  if (!headers) return new Response('Unauthorized', { status: 401 });
  const body = await req.text();
  const res = await fetch(`${API_BASE}/api/invites/accept`, {
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
