// Minimal SDK client for dev usage. Can be replaced by generated client later.

export type FetchOptions = {
  headers?: Record<string, string>;
};

export type Fetcher = (input: string | URL, init?: RequestInit) => Promise<Response>;

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

export function createClient(baseUrl: string, fetcher: Fetcher = fetch) {
  const base = baseUrl.replace(/\/$/, '');

  const buildUrl = (path: string) => `${base}${path}`;

  async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
    const url = buildUrl(path);
    const headers = {
      'Content-Type': 'application/json',
      ...(init.headers as Record<string, string> | undefined),
    };
    const res = await fetcher(url, { ...init, headers });
    if (!res.ok) {
      const text = await res.text().catch(() => '');
      throw new Error(`Request failed ${res.status}: ${text}`);
    }
    return (await res.json()) as T;
  }

  async function get<T>(path: string, opts?: FetchOptions): Promise<T> {
    const headers = {
      ...(opts?.headers ?? {}),
    };
    return request<T>(path, {
      method: 'GET',
      headers,
    });
  }

  return {
    me: (opts?: FetchOptions) => get<MeResponse>('/api/me', opts),
    tenants: (opts?: FetchOptions) => get<TenantSummary[]>('/api/tenants', opts),
  };
}
