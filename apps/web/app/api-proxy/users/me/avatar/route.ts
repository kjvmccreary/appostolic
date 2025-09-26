import { NextResponse } from 'next/server';
import { API_BASE } from '../../../../../src/lib/serverEnv';
import { buildProxyHeaders } from '../../../../../src/lib/proxyHeaders';

export const runtime = 'nodejs';

export async function POST(req: Request) {
  // Forward multipart/form-data as-is to the API with authenticated headers.
  const headers = await buildProxyHeaders();
  if (!headers) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });

  // Important: do NOT set content-type; fetch will set the correct multipart boundary
  // when passing a ReadableStream body.
  const target = `${API_BASE}/api/users/me/avatar`;
  // Reconstruct FormData to forward to API
  const form = await req.formData();
  // Normalize headers into a plain object to avoid issues with special header objects in test env
  const upstreamHeaders: Record<string, string> = { ...headers };
  // Remove JSON content-type from generic proxy headers so fetch can assign the multipart boundary.
  if (upstreamHeaders['Content-Type']) delete upstreamHeaders['Content-Type'];
  if (upstreamHeaders['content-type']) delete upstreamHeaders['content-type'];
  const res = await fetch(target, {
    method: 'POST',
    headers: upstreamHeaders,
    body: form as unknown as BodyInit,
  });

  const responseHeaders = new Headers({
    'content-type': res.headers.get('content-type') ?? 'application/json',
  });
  const location = res.headers.get('location');
  if (location) responseHeaders.set('location', location);

  // Buffer body to avoid issues with streaming in edge-like test environments
  const arrayBuffer = await res.arrayBuffer();
  return new NextResponse(arrayBuffer, { status: res.status, headers: responseHeaders });
}
