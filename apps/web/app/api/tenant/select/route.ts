import { NextRequest, NextResponse } from 'next/server';
import { getServerSession } from 'next-auth';
import { authOptions } from '../../../../src/lib/auth';

export const runtime = 'nodejs';

// Minimal server route to set a cookie for selected tenant.
export async function POST(req: NextRequest) {
  const body = (await req.json().catch(() => ({}))) as { tenant?: string; slug?: string };
  const candidate = (body.tenant || body.slug || '').trim();
  if (!candidate) {
    return NextResponse.json({ error: 'Invalid tenant' }, { status: 400 });
  }
  // Fetch session to validate that the requested tenant slug/id exists in memberships.
  const session = (await getServerSession(authOptions).catch(() => null)) as null | {
    memberships?: { tenantId: string; tenantSlug: string }[];
  };
  const memberships = session?.memberships || [];
  const match = memberships.find((m) => m.tenantSlug === candidate || m.tenantId === candidate);
  if (!match) {
    return NextResponse.json({ error: 'Invalid tenant' }, { status: 400 });
  }
  const value = match.tenantSlug; // always store canonical slug in cookie
  const res = NextResponse.json({ ok: true, tenant: value });
  const isHttps =
    req.headers.get('x-forwarded-proto') === 'https' || req.nextUrl.protocol === 'https:';
  res.cookies.set('selected_tenant', value, {
    path: '/',
    httpOnly: true,
    sameSite: 'lax',
    maxAge: 60 * 60 * 24 * 7,
    secure: isHttps,
  });
  return res;
}

// Support GET for convenience: /api/tenant/select?tenant=slug&next=/studio
export async function GET(req: NextRequest) {
  const url = new URL(req.url);
  const candidate = url.searchParams.get('tenant')?.trim() || '';
  const DEFAULT_NEXT = '/studio/agents';
  const rawNext = url.searchParams.get('next') || DEFAULT_NEXT;
  const next = rawNext.startsWith('/') && !rawNext.startsWith('//') ? rawNext : DEFAULT_NEXT;
  if (!candidate) {
    return NextResponse.json({ error: 'Invalid tenant' }, { status: 400 });
  }
  const session = (await getServerSession(authOptions).catch(() => null)) as null | {
    memberships?: { tenantId: string; tenantSlug: string }[];
  };
  const memberships = session?.memberships || [];
  const match = memberships.find((m) => m.tenantSlug === candidate || m.tenantId === candidate);
  if (!match) {
    return NextResponse.json({ error: 'Invalid tenant' }, { status: 400 });
  }
  const value = match.tenantSlug;
  const res = NextResponse.redirect(new URL(next, url.origin));
  const isHttps =
    req.headers.get('x-forwarded-proto') === 'https' || req.nextUrl.protocol === 'https:';
  res.cookies.set('selected_tenant', value, {
    path: '/',
    httpOnly: true,
    sameSite: 'lax',
    maxAge: 60 * 60 * 24 * 7,
    secure: isHttps,
  });
  return res;
}
