import { http, HttpResponse } from 'msw';

export type TenantSelectCall = {
  url: string;
  body: unknown;
  headers: Record<string, string>;
};

export type RefreshCall = {
  url: string;
  body: string;
  headers: Record<string, string>;
};

export type RefreshResponseFactory = () => {
  neutral: { token: string; expiresAt: number | string };
  tenant?: { token: string; expiresAt?: number | string };
  setCookie?: string | null;
};

// Provides the default neutral + tenant refresh payload mirrors the real NextAuth refresh endpoint.
function defaultRefreshResponse(): ReturnType<RefreshResponseFactory> {
  const expiresAt = Date.now() + 15 * 60 * 1000;
  return {
    neutral: { token: 'mock-neutral-access', expiresAt },
    tenant: { token: 'mock-tenant-access', expiresAt },
    setCookie: 'rt=mock-rotated; Path=/; HttpOnly; SameSite=Lax',
  };
}

const authMockState = {
  tenantSelect: {
    calls: [] as TenantSelectCall[],
  },
  refresh: {
    calls: [] as RefreshCall[],
    responseFactory: defaultRefreshResponse as RefreshResponseFactory,
  },
};

// Collects fetch Headers into a serializable record so tests can assert on auth headers succinctly.
function headersToRecord(headers: Headers): Record<string, string> {
  const entries: Record<string, string> = {};
  headers.forEach((value, key) => {
    entries[key] = value;
  });
  return entries;
}

// MSW handlers that fake the select-tenant and refresh auth endpoints used by the web app.
export const authHandlers = [
  http.post('http://*/api/tenant/select', async ({ request }) => {
    const headers = headersToRecord(request.headers);
    const body = await request.json().catch(() => ({}));
    const tenant = ((body as Record<string, unknown>).tenant ||
      (body as Record<string, unknown>).slug ||
      '') as string;
    const trimmed = (tenant ?? '').toString().trim();
    authMockState.tenantSelect.calls.push({ url: request.url, body, headers });
    if (!trimmed) {
      return HttpResponse.json({ error: 'Invalid tenant' }, { status: 400 });
    }
    return HttpResponse.json(
      { ok: true, tenant: trimmed },
      {
        status: 200,
        headers: { 'set-cookie': `selected_tenant=${trimmed}; Path=/; HttpOnly; SameSite=Lax` },
      },
    );
  }),
  http.post('http://*/api/auth/refresh', async ({ request }) => {
    const headers = headersToRecord(request.headers);
    const bodyText = await request.text();
    authMockState.refresh.calls.push({ url: request.url, body: bodyText, headers });
    const response = authMockState.refresh.responseFactory();
    const neutralExpires = response.neutral.expiresAt;
    const tenantExpires = response.tenant?.expiresAt ?? neutralExpires;
    const responseHeaders: Record<string, string> = {};
    if (response.setCookie) {
      responseHeaders['set-cookie'] = response.setCookie;
    }
    return HttpResponse.json(
      {
        access: {
          token: response.neutral.token,
          expiresAt: neutralExpires,
        },
        ...(response.tenant
          ? {
              tenantToken: {
                access: {
                  token: response.tenant.token,
                  expiresAt: tenantExpires,
                },
              },
            }
          : {}),
      },
      { status: 200, headers: responseHeaders },
    );
  }),
];

// Allows tests to swap in a custom refresh response (e.g., to simulate rotated cookies or errors).
export function configureRefreshResponse(factory: RefreshResponseFactory) {
  authMockState.refresh.responseFactory = factory;
}

// Clears captured calls and restores the default refresh response between tests.
export function resetAuthMocks() {
  authMockState.tenantSelect.calls.length = 0;
  authMockState.refresh.calls.length = 0;
  authMockState.refresh.responseFactory = defaultRefreshResponse;
}

export { authMockState };
