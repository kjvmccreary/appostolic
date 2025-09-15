import { NextResponse, NextRequest } from 'next/server';
import { getToken } from 'next-auth/jwt';

// Toggle enforcement: when false, allow everything (dev convenience).
const WEB_AUTH_ENABLED = (process.env.WEB_AUTH_ENABLED ?? 'false').toLowerCase() === 'true';

// Paths that require auth
const PROTECTED_MATCHERS = ['/studio', '/dev'];

export async function middleware(req: NextRequest) {
  // If auth enforcement is disabled, still surface x-pathname for layout logic
  if (!WEB_AUTH_ENABLED) {
    const res = NextResponse.next();
    res.headers.set('x-pathname', req.nextUrl.pathname);
    return res;
  }

  const { pathname, search } = req.nextUrl;
  const isProtected = PROTECTED_MATCHERS.some(
    (p) => pathname === p || pathname.startsWith(p + '/'),
  );
  // Note: we no longer auto-redirect away from /login server-side; client handles it.

  // Let next-auth infer the secret; passing an unset secret can break decoding in dev
  const token = await getToken({ req });
  const isAuthed = !!token?.email;

  // Redirect unauthenticated users hitting protected routes → /login?next=...
  if (isProtected && !isAuthed) {
    const loginUrl = req.nextUrl.clone();
    loginUrl.pathname = '/login';
    // Preserve the originally requested path and query
    const requested = pathname + (search ?? '');
    loginUrl.searchParams.set('next', requested);
    return NextResponse.redirect(loginUrl);
  }

  // Do not force-redirect authenticated users away from /login; the client page will handle
  // redirecting into the app when appropriate. This avoids bouncing immediately after logout
  // while session cookies are still clearing.

  const res = NextResponse.next();
  // Surface the current pathname to the layout via a header to selectively hide UI
  res.headers.set('x-pathname', pathname);
  return res;
}

// Apply only to selected paths; keep public routes untouched
export const config = {
  // Include /select-tenant so we can set x-pathname for the layout to hide the global TenantSwitcher there
  matcher: [
    '/studio/:path*',
    '/dev/:path*',
    '/login',
    '/select-tenant',
    '/magic/:path*',
    '/signup',
  ],
};
