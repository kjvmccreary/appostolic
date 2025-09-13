import { NextRequest, NextResponse } from 'next/server';

export const runtime = 'nodejs';

// Minimal server route to set a cookie for selected tenant.
export async function POST(req: NextRequest) {
  const { tenant } = await req.json().catch(() => ({ tenant: undefined }));
  if (!tenant || typeof tenant !== 'string' || tenant.trim() === '') {
    return NextResponse.json({ error: 'Invalid tenant' }, { status: 400 });
  }
  const res = NextResponse.json({ ok: true });
  // Cookie for 7 days, Lax, httpOnly for security; client can use session.update to reflect selection
  res.cookies.set('selected_tenant', tenant, {
    path: '/',
    httpOnly: true,
    sameSite: 'lax',
    maxAge: 60 * 60 * 24 * 7,
    secure: process.env.NODE_ENV === 'production',
  });
  return res;
}

// Support GET for convenience: /api/tenant/select?tenant=slug&next=/studio
export async function GET(req: NextRequest) {
  const url = new URL(req.url);
  const tenant = url.searchParams.get('tenant')?.trim();
  const DEFAULT_NEXT = '/studio/agents';
  const rawNext = url.searchParams.get('next') || DEFAULT_NEXT;
  // Only allow same-origin absolute paths starting with a single '/'; else fallback
  const next = rawNext.startsWith('/') && !rawNext.startsWith('//') ? rawNext : DEFAULT_NEXT;
  if (!tenant) {
    return NextResponse.json({ error: 'Invalid tenant' }, { status: 400 });
  }
  const res = NextResponse.redirect(new URL(next, url.origin));
  res.cookies.set('selected_tenant', tenant, {
    path: '/',
    httpOnly: true,
    sameSite: 'lax',
    maxAge: 60 * 60 * 24 * 7,
    secure: process.env.NODE_ENV === 'production',
  });
  return res;
}
