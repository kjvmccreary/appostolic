import { describe, it, expect, vi, beforeEach } from 'vitest';
import { POST, GET } from './route';
import { NextRequest } from 'next/server';

// Helper to build NextRequest
function buildRequest(url: string, init?: RequestInit) {
  const base = new Request(url, init);
  return new NextRequest(base);
}

// Mock getServerSession by monkeypatching the imported module logic indirectly.
vi.mock('../../../../src/lib/auth', () => ({ authOptions: {} }));
vi.mock('next-auth', async (orig) => {
  const actualMod = (await (orig as () => Promise<unknown>)()) as Record<string, unknown>;
  return {
    ...actualMod,
    getServerSession: vi.fn(async () => ({
      memberships: [
        { tenantId: 't-123', tenantSlug: 'kevin-personal-2' },
        { tenantId: 't-456', tenantSlug: 'org-alpha' },
      ],
    })),
  } as Record<string, unknown>;
});

// Access the mocked getServerSession to adjust behavior in specific tests
import { getServerSession } from 'next-auth';

describe('/api/tenant/select', () => {
  beforeEach(() => {
    (getServerSession as unknown as { mockClear: () => void }).mockClear();
  });

  it('POST rejects missing tenant', async () => {
    const req = buildRequest('http://localhost/api/tenant/select', {
      method: 'POST',
      body: JSON.stringify({}),
    });
    const res = await POST(req);
    expect(res.status).toBe(400);
  });

  it('POST accepts matching tenant slug', async () => {
    const req = buildRequest('http://localhost/api/tenant/select', {
      method: 'POST',
      body: JSON.stringify({ tenant: 'kevin-personal-2' }),
    });
    const res = await POST(req);
    expect(res.status).toBe(200);
    const json = await res.json();
    expect(json).toMatchObject({ ok: true, tenant: 'kevin-personal-2' });
  });

  it('POST rejects unknown tenant', async () => {
    const req = buildRequest('http://localhost/api/tenant/select', {
      method: 'POST',
      body: JSON.stringify({ tenant: 'does-not-exist' }),
    });
    const res = await POST(req);
    expect(res.status).toBe(400);
  });

  it('GET redirects and sets cookie for valid tenant', async () => {
    const req = buildRequest(
      'http://localhost/api/tenant/select?tenant=org-alpha&next=/studio/agents',
    );
    const res = await GET(req);
    expect(res.status).toBe(307); // NextResponse.redirect default
    const cookie = res.cookies.get('selected_tenant');
    expect(cookie?.value).toBe('org-alpha');
  });

  it('GET rejects invalid tenant', async () => {
    const req = buildRequest('http://localhost/api/tenant/select?tenant=bad-slug');
    const res = await GET(req);
    expect(res.status).toBe(400);
  });
});
