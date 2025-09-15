// Server-only helper to call our internal /api-proxy/* routes from server components
// - Builds an absolute URL from the incoming request (host + protocol)
// - Forwards cookies so auth session reaches the proxy route
// - Leaves caching options to callers (defaults to no-store for safety)

import { headers, cookies } from 'next/headers';

export type ServerFetchInit = RequestInit & {
  // Keep Next.js route options when needed
  next?: { revalidate?: number };
};

function safeGetHost(): { host: string | null; proto: string | null } {
  try {
    const h = headers();
    const xfHost = h.get('x-forwarded-host');
    const host = xfHost ?? h.get('host');
    const proto = h.get('x-forwarded-proto');
    return { host, proto };
  } catch {
    return { host: null, proto: null };
  }
}

function safeCookieString(): string {
  try {
    return cookies().toString();
  } catch {
    return '';
  }
}

function currentRequestBase(): string {
  const envBase = process.env.NEXT_PUBLIC_WEB_BASE;
  const { host, proto } = safeGetHost();
  if (envBase) {
    // Ensure no trailing slash
    return envBase.replace(/\/$/, '');
  }
  if (host) {
    const scheme = proto ?? (process.env.NODE_ENV === 'development' ? 'http' : 'https');
    return `${scheme}://${host}`;
  }
  // Test or non-Next context fallback
  return 'http://localhost:3000';
}

export function withAbsoluteUrl(input: string | URL): string {
  const base = currentRequestBase();
  if (typeof input === 'string') {
    if (input.startsWith('http://') || input.startsWith('https://')) return input;
    // Support both "/api-proxy/..." and "api-proxy/..."
    const path = input.startsWith('/') ? input : `/${input}`;
    return `${base}${path}`;
  }
  // URL instance may be relative or absolute; normalize relative against base
  try {
    return new URL(input as unknown as string, base).toString();
  } catch {
    return `${base}/${String(input)}`;
  }
}

export async function fetchFromProxy(input: string | URL, init: ServerFetchInit = {}) {
  const url = withAbsoluteUrl(input);
  const cookieHeader = safeCookieString();
  const mergedHeaders: HeadersInit | undefined = cookieHeader
    ? { ...(init.headers || {}), cookie: cookieHeader }
    : init.headers;

  // Normalize caching options to avoid specifying both `cache` and `next.revalidate`.
  // Default to `no-store` for safety. If callers pass a specific `next.revalidate`,
  // we'll honor it only when `cache` isn't explicitly 'no-store'.
  const cacheMode: RequestCache = (init.cache as RequestCache) ?? 'no-store';
  const nextOptions = cacheMode === 'no-store' ? undefined : init.next;

  // Exclude original `next`/`headers` so our sanitized values win, then spread the rest.
  // Omit fields we handle separately using typed destructuring.
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  const { next: _omitNext, headers: _omitHeaders, ...rest } = init as ServerFetchInit;

  const res = await fetch(url, {
    ...rest,
    // Default to no-store for dynamic, authed server fetches. Avoid also setting
    // next.revalidate simultaneously, which Next.js warns about.
    cache: cacheMode,
    ...(nextOptions ? { next: nextOptions } : {}),
    headers: mergedHeaders,
  } as ServerFetchInit);
  return res;
}
