import { cookies, headers as nextHeaders } from 'next/headers';
import { NextResponse } from 'next/server';
import { getServerSession } from 'next-auth';
import type { Session } from 'next-auth';
import { authOptions } from './auth';
import { API_BASE } from './serverEnv';
import { parseSetCookie, extractSetCookieValues, type CookieSetterOptions } from './cookieUtils';

const DEBUG_PROXY_HEADERS = process.env.NODE_ENV !== 'production';

function debugProxy(message: string, context?: Record<string, unknown>) {
  if (!DEBUG_PROXY_HEADERS) return;
  const payload = context ? ` ${JSON.stringify(context)}` : '';
  console.warn(`[proxyHeaders] ${message}${payload}`);
}

const WEB_AUTH_ENABLED = (process.env.WEB_AUTH_ENABLED ?? 'false').toLowerCase() === 'true';
const TENANT_COOKIE = 'selected_tenant';
const ACCESS_SKEW_MS = 60_000;

type RefreshAccessPayload = {
  access?: { token?: string; expiresAt?: string | number };
  tenantToken?: { access?: { token?: string; expiresAt?: string | number } };
};

type CachedAccess = {
  token: string;
  expiresAt: number;
};

type RefreshOutcome = {
  primary: CachedAccess;
  neutral?: CachedAccess | null;
  tenant?: CachedAccess | null;
};

type RefreshPromiseResult = {
  outcome: RefreshOutcome | null;
  cookies: ProxyCookie[];
  tenantParam: string | null;
  sessionKey: string | null;
};

const tokenCache = new Map<string, CachedAccess>();
const inflight = new Map<string, Promise<RefreshPromiseResult>>();

function maskToken(value?: string | null): string | null {
  if (!value) return value ?? null;
  if (value.length <= 8) return value;
  return `${value.slice(0, 4)}…${value.slice(-4)}`;
}

function extractRefreshCookie(header: string): string | null {
  const match = header.match(/(?:^|;\s*)rt=([^;]+)/);
  return match ? match[1] : null;
}

function getSessionTenant(session: Session | null): string | undefined {
  const s = session as unknown as { tenant?: string } | null;
  return s?.tenant;
}

type RequestCookieStore = ReturnType<typeof cookies>;

function getSessionCookieValue(jar: RequestCookieStore): string | undefined {
  const configuredName = authOptions.cookies?.sessionToken?.name;
  const candidates = new Set(
    [configuredName, '__Secure-next-auth.session-token', 'next-auth.session-token'].filter(
      Boolean,
    ) as string[],
  );
  for (const name of candidates) {
    const value = jar.get(name)?.value;
    if (value) return value;
  }
  return undefined;
}

function parseExpires(value: string | number | undefined): number | null {
  if (typeof value === 'number') return value;
  if (typeof value === 'string') {
    const ms = Date.parse(value);
    return Number.isNaN(ms) ? null : ms;
  }
  return null;
}

export type ProxyHeaders = Record<string, string>;

export type ProxyCookie = {
  name: string;
  value: string;
  options: CookieSetterOptions;
};

type RefreshResult = {
  outcome: RefreshOutcome | null;
  cookies: ProxyCookie[];
};

export type ProxyHeadersContext = {
  headers: ProxyHeaders;
  cookies: ProxyCookie[];
};

type RotationBridgeEntry = {
  cookie: ProxyCookie;
  expiresAt: number;
};

const ROTATION_BRIDGE_TTL_MS = 10_000;
const rotationBridge = new Map<string, RotationBridgeEntry>();

function pruneRotationBridge(now = Date.now()) {
  for (const [key, entry] of rotationBridge) {
    if (entry.expiresAt <= now) rotationBridge.delete(key);
  }
}

function rememberRotation(oldValue: string, cookie: ProxyCookie) {
  pruneRotationBridge();
  rotationBridge.set(oldValue, { cookie, expiresAt: Date.now() + ROTATION_BRIDGE_TTL_MS });
}

function adoptRotatedCookie(
  currentValue: string | null,
  jar: RequestCookieStore,
  responseCookies: ProxyCookie[],
): string | null {
  pruneRotationBridge();
  if (!currentValue) return currentValue;
  const entry = rotationBridge.get(currentValue);
  if (!entry) return currentValue;
  jar.set(entry.cookie.name, entry.cookie.value, entry.cookie.options);
  const alreadyQueued = responseCookies.some(
    (c) => c.name === entry.cookie.name && c.value === entry.cookie.value,
  );
  if (!alreadyQueued) {
    responseCookies.push(entry.cookie);
  }
  debugProxy('bridged rotated refresh cookie', {
    previous: maskToken(currentValue),
    next: maskToken(entry.cookie.value),
  });
  return entry.cookie.value;
}

async function refreshToken(
  tenantParam: string | null,
  cookieHeader: string,
  cookieSetter: RequestCookieStore,
): Promise<RefreshResult> {
  const cookiesToSet: ProxyCookie[] = [];
  const url = new URL('/api/auth/refresh', API_BASE);
  if (tenantParam) url.searchParams.set('tenant', tenantParam);
  const incoming = nextHeaders();
  const forwardHeaders: Record<string, string> = {
    cookie: cookieHeader,
    'content-type': 'application/json',
  };
  const forwardedProto = incoming.get('x-forwarded-proto');
  if (forwardedProto) forwardHeaders['x-forwarded-proto'] = forwardedProto;
  const forwardedFor = incoming.get('x-forwarded-for');
  if (forwardedFor) forwardHeaders['x-forwarded-for'] = forwardedFor;
  const sessionFp = incoming.get('x-session-fp');
  if (sessionFp) forwardHeaders['x-session-fp'] = sessionFp;
  const sessionDevice = incoming.get('x-session-device');
  if (sessionDevice) forwardHeaders['x-session-device'] = sessionDevice;

  const refreshBefore = extractRefreshCookie(cookieHeader);
  debugProxy('refresh request start', {
    tenant: tenantParam ?? 'neutral',
    hasRt: Boolean(refreshBefore),
    rt: maskToken(refreshBefore),
  });

  const response = await fetch(url.toString(), {
    method: 'POST',
    headers: forwardHeaders,
    body: '{}',
    cache: 'no-store',
  }).catch((error: unknown) => {
    debugProxy('refresh request failed', {
      tenant: tenantParam ?? 'neutral',
      error: error instanceof Error ? error.message : String(error),
    });
    return null;
  });
  if (!response)
    return {
      outcome: null,
      cookies: cookiesToSet,
    };
  if (!response.ok) {
    let body: string | null = null;
    let failureCode: string | undefined;
    try {
      body = await response.text();
    } catch {
      body = null;
    }
    if (body) {
      try {
        const parsed = JSON.parse(body) as { code?: string };
        failureCode = parsed.code;
        if (failureCode === 'refresh_reuse' || failureCode === 'refresh_invalid') {
          try {
            cookieSetter.delete('rt');
            cookiesToSet.push({ name: 'rt', value: '', options: { path: '/', maxAge: 0 } });
            const sessionCookieNames = [
              authOptions.cookies?.sessionToken?.name,
              '__Secure-next-auth.session-token',
              'next-auth.session-token',
            ].filter(Boolean) as string[];
            for (const name of sessionCookieNames) {
              if (cookieSetter.get(name)) {
                cookieSetter.delete(name);
                cookiesToSet.push({ name, value: '', options: { path: '/', maxAge: 0 } });
              }
            }
            debugProxy('cleared refresh cookie after failure', {
              tenant: tenantParam ?? 'neutral',
              code: failureCode,
            });
          } catch (err) {
            debugProxy('failed to clear refresh cookie', {
              tenant: tenantParam ?? 'neutral',
              error: err instanceof Error ? err.message : String(err),
            });
          }
        }
      } catch {
        failureCode = undefined;
      }
    }
    debugProxy('refresh response not ok', {
      status: response.status,
      tenant: tenantParam ?? 'neutral',
      body,
      code: failureCode,
    });
    return { outcome: null, cookies: cookiesToSet };
  }

  const rawSetCookie = extractSetCookieValues(response.headers);
  let rotatedRt: string | null = null;
  let rotatedCookie: ProxyCookie | null = null;
  for (const entry of rawSetCookie) {
    const parsed = parseSetCookie(entry);
    if (parsed && parsed.name === 'rt') {
      cookieSetter.set(parsed.name, parsed.value, parsed.options);
      rotatedRt = parsed.value;
      rotatedCookie = { name: parsed.name, value: parsed.value, options: parsed.options };
      cookiesToSet.push(rotatedCookie);
    }
  }

  debugProxy('refresh response ok', {
    tenant: tenantParam ?? 'neutral',
    rotatedRt: maskToken(rotatedRt),
    previousRt: maskToken(refreshBefore),
  });

  if (refreshBefore && rotatedCookie) {
    rememberRotation(refreshBefore, rotatedCookie);
  }

  let payload: RefreshAccessPayload;
  try {
    payload = (await response.json()) as RefreshAccessPayload;
  } catch {
    return { outcome: null, cookies: cookiesToSet };
  }

  const candidate = tenantParam ? payload.tenantToken?.access : payload.access;
  const fallback = payload.access;

  const primaryToken = candidate?.token ?? fallback?.token;
  const primaryExpires = parseExpires(candidate?.expiresAt ?? fallback?.expiresAt);
  if (!primaryToken || !primaryExpires)
    return {
      outcome: null,
      cookies: cookiesToSet,
    };
  const primary: CachedAccess = { token: primaryToken, expiresAt: primaryExpires };

  const fallbackExpires = parseExpires(fallback?.expiresAt);
  const neutral: CachedAccess | null =
    fallback?.token && fallbackExpires
      ? { token: fallback.token, expiresAt: fallbackExpires }
      : tenantParam
        ? null
        : primary;

  let tenantAccess: CachedAccess | null = null;
  if (tenantParam && payload.tenantToken?.access?.token) {
    const tenantExpires = parseExpires(payload.tenantToken.access.expiresAt ?? fallback?.expiresAt);
    if (tenantExpires) {
      tenantAccess = { token: payload.tenantToken.access.token, expiresAt: tenantExpires };
    }
  }

  return {
    outcome: { primary, neutral, tenant: tenantAccess },
    cookies: cookiesToSet,
  };
}

function makeSessionBase(sessionKey: string | null): string {
  return sessionKey ?? 'anon';
}

function buildCacheKey(sessionKey: string | null, tenantSlug: string): string {
  return `${makeSessionBase(sessionKey)}|${tenantSlug}`;
}

function isValid(cached: CachedAccess | undefined): cached is CachedAccess {
  return !!cached && cached.expiresAt - ACCESS_SKEW_MS > Date.now();
}

// Collapse refresh responses into shared cache entries so concurrent neutral/tenant
// requests reuse the same rotation without triggering reuse detection upstream.
function applyOutcome(
  sessionKey: string | null,
  tenantParam: string | null,
  outcome: RefreshOutcome | null,
) {
  if (!outcome) return;
  const neutralKey = buildCacheKey(sessionKey, 'neutral');
  if (!tenantParam) {
    tokenCache.set(neutralKey, outcome.primary);
    return;
  }
  const tenantKey = buildCacheKey(sessionKey, tenantParam);
  if (outcome.tenant) tokenCache.set(tenantKey, outcome.tenant);
  else tokenCache.set(tenantKey, outcome.primary);
  if (outcome.neutral) tokenCache.set(neutralKey, outcome.neutral);
}

async function getOrRefreshAccess(
  cacheKey: string,
  tenantParam: string | null,
  cookieStore: RequestCookieStore,
  sessionKey: string | null,
  refreshCookie: string | null,
  responseCookies: ProxyCookie[],
): Promise<CachedAccess | null> {
  const inflightKey = sessionKey ? `sess:${sessionKey}` : `rt:${refreshCookie ?? 'none'}`;

  for (let attempt = 0; attempt < 2; attempt++) {
    const cached = tokenCache.get(cacheKey);
    if (isValid(cached)) return cached;
    if (cached) tokenCache.delete(cacheKey);

    const existing = inflight.get(inflightKey);
    if (existing) {
      const {
        outcome,
        cookies,
        tenantParam: inflightTenant,
        sessionKey: inflightSession,
      } = await existing;
      applyOutcome(inflightSession ?? sessionKey, inflightTenant, outcome);
      if (cookies.length > 0) responseCookies.push(...cookies);
      continue;
    }

    const cookieHeader = cookieStore.toString();
    const refreshPromise = (async () => {
      const refreshResult = await refreshToken(tenantParam, cookieHeader, cookieStore);
      applyOutcome(sessionKey, tenantParam, refreshResult.outcome);
      return {
        outcome: refreshResult.outcome,
        cookies: refreshResult.cookies,
        tenantParam,
        sessionKey,
      } satisfies RefreshPromiseResult;
    })();
    inflight.set(inflightKey, refreshPromise);
    try {
      const { outcome, cookies } = await refreshPromise;
      if (cookies.length > 0) responseCookies.push(...cookies);
      if (!outcome) return null;
    } finally {
      inflight.delete(inflightKey);
    }
  }

  const final = tokenCache.get(cacheKey);
  return isValid(final) ? final : null;
}

/**
 * Build headers for proxying to the API.
 * - When WEB_AUTH_ENABLED=true, requires a signed-in session (email) always.
 * - By default also requires a selected tenant (session.tenant or cookie), returning null if missing.
 * - For special endpoints that allow user-only auth (e.g., invite acceptance),
 *   pass { requireTenant: false } to proceed without tenant-specific tokens.
 * - Neutral or tenant-scoped access tokens are cached per session to limit refresh rotations.
 */
export async function buildProxyHeaders(options?: {
  requireTenant?: boolean;
}): Promise<ProxyHeadersContext | null> {
  const requireTenant = options?.requireTenant ?? true;

  // Always prefer an authenticated web session if present, regardless of WEB_AUTH_ENABLED flag.
  // This makes magic-link flows work in dev without toggling envs.
  const session = await getServerSession(authOptions).catch(() => null);
  const emailFromSession = session?.user?.email;
  if (!emailFromSession) {
    debugProxy('missing session email');
    // Auth required but no session → unauthorized (when enforcement enabled) or null fallback.
    return WEB_AUTH_ENABLED ? null : null;
  }

  const jar = cookies();
  const responseCookies: ProxyCookie[] = [];
  const sessionTenant = getSessionTenant(session as Session);
  const cookieTenant = jar.get(TENANT_COOKIE)?.value;
  const tenant = sessionTenant || cookieTenant || null;
  if (requireTenant && !tenant) {
    debugProxy('tenant required but missing', { sessionTenant, cookieTenant });
    return null;
  }

  const refreshCookieRaw = jar.get('rt')?.value ?? null;
  const refreshCookie = adoptRotatedCookie(refreshCookieRaw, jar, responseCookies);
  if (!refreshCookie) {
    debugProxy('missing refresh cookie');
    return null;
  }

  const sessionKey = getSessionCookieValue(jar);
  const cacheKey = sessionKey
    ? `${sessionKey}|${tenant ?? 'neutral'}`
    : `anon|${tenant ?? 'neutral'}`;
  const tenantParam = tenant && requireTenant ? tenant : null;
  const access = await getOrRefreshAccess(
    cacheKey,
    tenantParam,
    jar,
    sessionKey ?? null,
    refreshCookie ?? null,
    responseCookies,
  );
  if (!access) {
    debugProxy('failed to obtain access token', { tenant: tenantParam ?? 'neutral' });
    return null;
  }

  return {
    headers: {
      Authorization: `Bearer ${access.token}`,
      'Content-Type': 'application/json',
    },
    cookies: responseCookies,
  };
}

/**
 * Apply any cookies captured during proxy header construction onto a Next.js response.
 * Ensures refresh rotations and cookie deletions propagate back to the browser.
 */
export function applyProxyCookies<T extends NextResponse>(
  response: T,
  context: ProxyHeadersContext | null | undefined,
): T {
  if (!context || context.cookies.length === 0) return response;
  for (const cookie of context.cookies) {
    response.cookies.set(cookie.name, cookie.value, cookie.options);
  }
  return response;
}
