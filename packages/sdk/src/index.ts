// Minimal SDK client for dev usage. Can be replaced by generated client later.
export type DevHeaders = {
  'x-dev-user': string;
  'x-tenant': string;
};

export type FetchOptions = {
  headers?: Record<string, string>;
};

export type MeResponse = {
  sub: string;
  email: string;
  tenant_id: string;
  tenant_slug: string;
};

export type TenantSummary = {
  Id: string; // Guid as string
  Name: string;
};

export function createClient(baseUrl: string, defaultHeaders: Record<string, string> = {}) {
  const base = baseUrl.replace(/\/$/, '');

  async function get<T>(path: string, opts?: FetchOptions): Promise<T> {
    const res = await fetch(`${base}${path}`, {
      method: 'GET',
      headers: { 'Content-Type': 'application/json', ...defaultHeaders, ...(opts?.headers || {}) },
    });
    if (!res.ok) {
      const text = await res.text().catch(() => '');
      throw new Error(`Request failed ${res.status}: ${text}`);
    }
    return res.json() as Promise<T>;
  }

  return {
    me: (opts?: FetchOptions) => get<MeResponse>('/api/me', opts),
    tenants: (opts?: FetchOptions) => get<TenantSummary[]>('/api/tenants', opts),
  };
}
