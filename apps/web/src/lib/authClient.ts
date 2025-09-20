// Lightweight frontend auth client for neutral access token management.
// Story 4: shift to httpOnly refresh cookie (rt) set by API; only access token is held in memory.
// This module exposes initialize, getAccessToken, withAuthFetch (wrapper), and a refresh flow.

export interface AuthTokens {
  accessToken: string;
  accessExpiresAt: number; // epoch ms
}

let current: AuthTokens | null = null;
let refreshing: Promise<void> | null = null;

const ACCESS_SKEW_MS = 30_000; // proactively refresh 30s before expiry

function isExpired(t: AuthTokens | null) {
  if (!t) return true;
  return Date.now() + ACCESS_SKEW_MS >= t.accessExpiresAt;
}

async function refresh(): Promise<void> {
  try {
    const p = fetch('/api/_auth/refresh-neutral', {
      method: 'POST',
      credentials: 'include', // send refresh cookie
      headers: { 'content-type': 'application/json' },
    })
      .then(async (r) => {
        if (!r.ok) throw new Error('refresh failed');
        const data = (await r.json()) as {
          access?: { token: string; expiresAt: string | number; type: string };
        };
        if (!data.access?.token) throw new Error('missing access token');
        const exp =
          typeof data.access.expiresAt === 'string'
            ? Date.parse(data.access.expiresAt)
            : (data.access.expiresAt as number);
        current = { accessToken: data.access.token, accessExpiresAt: exp };
      })
      .finally(() => {
        refreshing = null;
      });
    refreshing = p;
    await p;
  } catch (err) {
    current = null; // force re-login path
    refreshing = null;
    throw err;
  }
}

export async function getAccessToken(): Promise<string | null> {
  if (isExpired(current)) {
    if (!refreshing) {
      await refresh();
    } else {
      await refreshing; // wait in-flight
    }
  }
  return current?.accessToken ?? null;
}

export async function withAuthFetch(
  input: RequestInfo | URL,
  init: RequestInit = {},
): Promise<Response> {
  const token = await getAccessToken().catch(() => null);
  const headers = new Headers(init.headers || {});
  if (token) headers.set('Authorization', `Bearer ${token}`);
  return fetch(input, { ...init, headers, credentials: 'include' });
}

export function primeNeutralAccess(token: string, expiresAt: string | number) {
  const exp = typeof expiresAt === 'string' ? Date.parse(expiresAt) : (expiresAt as number);
  current = { accessToken: token, accessExpiresAt: exp };
}
