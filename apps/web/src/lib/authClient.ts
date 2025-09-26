// Lightweight frontend auth client for neutral access token management.
// Story 8: silent refresh loop + retry-once logic.
//  - Access token held only in memory.
//  - httpOnly refresh cookie (rt) supplied automatically by browser.
//  - We proactively refresh ~60s before expiry (configurable skew) and on-demand when 401 occurs.
//  - Single-flight refresh: concurrent callers await the same promise.
//  - Public helpers: primeNeutralAccess, getAccessToken, withAuthFetch, forceRefresh, startAutoRefresh, stopAutoRefresh.

export interface AuthTokens {
  accessToken: string;
  accessExpiresAt: number; // epoch ms
}

let current: AuthTokens | null = null;
let refreshing: Promise<void> | null = null;

const ACCESS_SKEW_MS = 60_000; // proactive refresh threshold (1 min early)
let timer: ReturnType<typeof setTimeout> | null = null;
let autoEnabled = false;

function isExpired(t: AuthTokens | null) {
  if (!t) return true;
  return Date.now() + ACCESS_SKEW_MS >= t.accessExpiresAt;
}

function schedule() {
  if (!autoEnabled || !current) return;
  const now = Date.now();
  const target = current.accessExpiresAt - ACCESS_SKEW_MS;
  const delay = Math.max(5_000, target - now); // minimum 5s safety
  if (timer) clearTimeout(timer);
  timer = setTimeout(() => {
    // fire and forget; errors will clear token, subsequent calls will redirect/login
    refresh().catch(() => {});
  }, delay);
}

async function refresh(): Promise<void> {
  try {
    const p = fetch('/api/auth/refresh', {
      method: 'POST',
      credentials: 'include',
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
        schedule();
      })
      .finally(() => {
        refreshing = null;
      });
    refreshing = p;
    await p;
  } catch (err) {
    current = null;
    refreshing = null;
    throw err;
  }
}

export function startAutoRefresh() {
  autoEnabled = true;
  schedule();
}

export function stopAutoRefresh() {
  autoEnabled = false;
  if (timer) clearTimeout(timer);
  timer = null;
}

export async function forceRefresh() {
  await refresh();
  return current?.accessToken ?? null;
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
  const resp = await fetch(input, { ...init, headers, credentials: 'include' });
  if (resp.status === 401) {
    // Retry once after a forced refresh (handles race: token expired between check & request)
    try {
      await forceRefresh();
      const retryHeaders = new Headers(init.headers || {});
      const newTok = current?.accessToken;
      if (newTok) retryHeaders.set('Authorization', `Bearer ${newTok}`);
      return await fetch(input, { ...init, headers: retryHeaders, credentials: 'include' });
    } catch {
      return resp; // return original 401 if refresh fails
    }
  }
  return resp;
}

export function primeNeutralAccess(token: string, expiresAt: string | number) {
  const exp = typeof expiresAt === 'string' ? Date.parse(expiresAt) : (expiresAt as number);
  current = { accessToken: token, accessExpiresAt: exp };
  if (autoEnabled) schedule();
}
