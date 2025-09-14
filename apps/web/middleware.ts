import { NextResponse, NextRequest } from 'next/server';
import { getToken } from 'next-auth/jwt';

// Toggle enforcement: when false, allow everything (dev convenience).
const WEB_AUTH_ENABLED = (process.env.WEB_AUTH_ENABLED ?? 'false').toLowerCase() === 'true';

// Paths that require auth
const PROTECTED_MATCHERS = ['/studio', '/dev'];

export async function middleware(req: NextRequest) {
  // Short-circuit if auth enforcement is disabled
  if (!WEB_AUTH_ENABLED) return NextResponse.next();

  const { pathname, search } = req.nextUrl;
  const isProtected = PROTECTED_MATCHERS.some(
    (p) => pathname === p || pathname.startsWith(p + '/'),
  );
  const isLogin = pathname === '/login';

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

  // Redirect authenticated users away from /login → default app page
  if (isLogin && isAuthed) {
    const dest = req.nextUrl.clone();
    dest.pathname = '/studio/agents';
    dest.search = '';
    return NextResponse.redirect(dest);
  }

  const res = NextResponse.next();
  // Surface the current pathname to the layout via a header to selectively hide UI
  res.headers.set('x-pathname', pathname);
  return res;
}

// Apply only to selected paths; keep public routes untouched
export const config = {
  matcher: ['/studio/:path*', '/dev/:path*', '/login'],
};
