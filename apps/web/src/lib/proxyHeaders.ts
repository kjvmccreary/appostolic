import { cookies, headers as nextHeaders } from 'next/headers';
import { getServerSession } from 'next-auth';
import type { Session } from 'next-auth';
import { authOptions } from './auth';
import { API_BASE } from './serverEnv';

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

const tokenCache = new Map<string, CachedAccess>();
const inflight = new Map<string, Promise<CachedAccess | null>>();

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

type CookieSetterOptions = {
  path?: string;
  httpOnly?: boolean;
  secure?: boolean;
  sameSite?: 'strict' | 'lax' | 'none';
  expires?: Date;
  maxAge?: number;
};

function parseSetCookie(
  header: string,
): { name: string; value: string; options: CookieSetterOptions } | null {
  const segments = header
    .split(';')
    .map((part) => part.trim())
    .filter(Boolean);
  if (segments.length === 0) return null;
  const [nameValue, ...attrs] = segments;
  const eqIndex = nameValue.indexOf('=');
  if (eqIndex <= 0) return null;
  const name = nameValue.slice(0, eqIndex).trim();
  const value = nameValue.slice(eqIndex + 1);
  const options: CookieSetterOptions = {};
  for (const attr of attrs) {
    const [rawKey, ...rawValParts] = attr.split('=');
    const key = rawKey.trim().toLowerCase();
    const val = rawValParts.join('=');
    switch (key) {
      case 'path':
        options.path = val || '/';
        break;
      case 'expires':
        if (val) {
          const date = new Date(val);
          if (!Number.isNaN(date.valueOf())) options.expires = date;
        }
        break;
      case 'max-age':
        options.maxAge = val ? Number(val) : undefined;
        break;
      case 'samesite':
        if (val) {
          const lower = val.toLowerCase();
          if (lower === 'lax' || lower === 'strict' || lower === 'none') {
            options.sameSite = lower as CookieSetterOptions['sameSite'];
          }
        }
        break;
      case 'secure':
        options.secure = true;
        break;
      case 'httponly':
        options.httpOnly = true;
        break;
      default:
        break;
    }
  }
  return { name, value, options };
}

async function refreshToken(
  tenantParam: string | null,
  cookieHeader: string,
  cookieSetter: RequestCookieStore,
): Promise<CachedAccess | null> {
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

  const response = await fetch(url.toString(), {
    method: 'POST',
    headers: forwardHeaders,
    body: '{}',
    cache: 'no-store',
  }).catch(() => null);
  if (!response || !response.ok) return null;

  const setCookieFn = (response.headers as unknown as { getSetCookie?: () => string[] })
    .getSetCookie;
  const setCookieValues = setCookieFn?.call(response.headers) ?? [];
  const rawSetCookie =
    setCookieValues.length > 0
      ? setCookieValues
      : (() => {
          const fallback = response.headers.get('set-cookie');
          return fallback ? [fallback] : [];
        })();
  for (const entry of rawSetCookie) {
    const parsed = parseSetCookie(entry);
    if (parsed && parsed.name === 'rt') {
      cookieSetter.set(parsed.name, parsed.value, parsed.options);
    }
  }

  let payload: RefreshAccessPayload;
  try {
    payload = (await response.json()) as RefreshAccessPayload;
  } catch {
    return null;
  }

  const candidate = tenantParam ? payload.tenantToken?.access : payload.access;
  const fallback = payload.access;
  const token = candidate?.token ?? fallback?.token;
  if (!token) return null;
  const expires = parseExpires(candidate?.expiresAt ?? fallback?.expiresAt);
  if (!expires) return null;
  return { token, expiresAt: expires };
}

async function getOrRefreshAccess(
  cacheKey: string,
  tenantParam: string | null,
  cookieHeader: string,
  cookieSetter: RequestCookieStore,
): Promise<CachedAccess | null> {
  const cached = tokenCache.get(cacheKey);
  if (cached && cached.expiresAt - ACCESS_SKEW_MS > Date.now()) {
    return cached;
  }
  tokenCache.delete(cacheKey);
  const pending = inflight.get(cacheKey);
  if (pending) {
    const result = await pending;
    if (result) tokenCache.set(cacheKey, result);
    return result;
  }
  const refreshPromise = refreshToken(tenantParam, cookieHeader, cookieSetter).finally(() => {
    inflight.delete(cacheKey);
  });
  inflight.set(cacheKey, refreshPromise);
  const result = await refreshPromise;
  if (result) tokenCache.set(cacheKey, result);
  return result;
}

export type ProxyHeaders = Record<string, string>;

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
}): Promise<ProxyHeaders | null> {
  const requireTenant = options?.requireTenant ?? true;

  // Always prefer an authenticated web session if present, regardless of WEB_AUTH_ENABLED flag.
  // This makes magic-link flows work in dev without toggling envs.
  const session = await getServerSession(authOptions).catch(() => null);
  const emailFromSession = session?.user?.email;
  if (!emailFromSession) {
    // Auth required but no session → unauthorized (when enforcement enabled) or null fallback.
    return WEB_AUTH_ENABLED ? null : null;
  }

  const jar = cookies();
  const sessionTenant = getSessionTenant(session as Session);
  const cookieTenant = jar.get(TENANT_COOKIE)?.value;
  const tenant = sessionTenant || cookieTenant || null;
  if (requireTenant && !tenant) {
    return null;
  }

  const refreshCookie = jar.get('rt')?.value;
  if (!refreshCookie) {
    return null;
  }

  const sessionKey = getSessionCookieValue(jar);
  const cacheKey = sessionKey
    ? `${sessionKey}|${tenant ?? 'neutral'}`
    : `anon|${tenant ?? 'neutral'}`;
  const cookieHeader = jar.toString();
  const tenantParam = tenant && requireTenant ? tenant : null;
  const access = await getOrRefreshAccess(cacheKey, tenantParam, cookieHeader, jar);
  if (!access) return null;

  return {
    Authorization: `Bearer ${access.token}`,
    'Content-Type': 'application/json',
  };
}
