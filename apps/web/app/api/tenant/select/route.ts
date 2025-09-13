import { NextRequest, NextResponse } from 'next/server';

export const runtime = 'nodejs';

// Minimal server route to set a cookie for selected tenant.
export async function POST(req: NextRequest) {
  const { tenant } = await req.json().catch(() => ({ tenant: undefined }));
  if (!tenant || typeof tenant !== 'string' || tenant.trim() === '') {
    return NextResponse.json({ error: 'Invalid tenant' }, { status: 400 });
  }
  const res = NextResponse.json({ ok: true });
  // Cookie for 7 days, Lax, httpOnly=false so client can read current selection if needed
  res.cookies.set('selected_tenant', tenant, {
    path: '/',
    httpOnly: false,
    sameSite: 'lax',
    maxAge: 60 * 60 * 24 * 7,
  });
  return res;
}
