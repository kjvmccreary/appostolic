import { NextRequest, NextResponse } from 'next/server';
import { API_BASE } from '../../../../src/lib/serverEnv';

export const runtime = 'nodejs';

export async function POST(req: NextRequest) {
  const target = `${API_BASE}/api/auth/reset-password`;
  const body = await req.text();
  const res = await fetch(target, {
    method: 'POST',
    body,
    headers: { 'content-type': 'application/json' },
  });
  return new NextResponse(null, { status: res.status });
}
