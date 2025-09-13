import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../src/lib/serverEnv';

export const runtime = 'nodejs';

// Anonymous proxy: forwards signup to the API to avoid browser CORS
export async function POST(req: NextRequest) {
  const target = `${API_BASE}/api/auth/signup`;
  const body = await req.text();
  const contentType = req.headers.get('content-type') ?? 'application/json';

  const res = await fetch(target, {
    method: 'POST',
    headers: { 'content-type': contentType },
    body,
  });

  const responseHeaders = new Headers({
    'content-type': res.headers.get('content-type') ?? 'application/json',
  });
  const location = res.headers.get('location');
  if (location) responseHeaders.set('location', location);

  return new NextResponse(res.body, { status: res.status, headers: responseHeaders });
}
