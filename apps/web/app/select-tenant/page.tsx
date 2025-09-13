import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { getServerSession } from 'next-auth';
import { authOptions } from '../../src/lib/auth';
import type { Session } from 'next-auth';

const TENANT_COOKIE = 'selected_tenant';

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

export default async function SelectTenantPage({
  searchParams,
}: {
  searchParams: Record<string, string | string[] | undefined>;
}) {
  const session = await getServerSession(authOptions);
  if (!session?.user?.email) {
    redirect('/login');
  }
  type Membership = { tenantId: string; tenantSlug: string; role: string };
  const memberships = (session as Session & { memberships?: Membership[] }).memberships ?? [];
  const c = cookies();
  const cookieTenant = c.get(TENANT_COOKIE)?.value;

  // Determine the intended destination after tenant selection.
  const rawNext =
    (Array.isArray(searchParams?.next) ? searchParams.next[0] : searchParams?.next) ?? undefined;
  const DEFAULT_NEXT = '/studio/agents';
  const next = (() => {
    if (!rawNext) return DEFAULT_NEXT;
    const n = String(rawNext).trim();
    // Only allow same-origin absolute paths starting with a single '/'
    if (!n.startsWith('/') || n.startsWith('//')) return DEFAULT_NEXT;
    return n;
  })();

  if (memberships && memberships.length === 1) {
    // Auto-select single membership via API route that sets cookie, then redirect
    const tenant = memberships[0].tenantSlug;
    redirect(
      `/api/tenant/select?tenant=${encodeURIComponent(tenant)}&next=${encodeURIComponent(next)}`,
    );
  }

  async function chooseTenant(formData: FormData) {
    'use server';
    const slug = String(formData.get('tenant'));
    const nextFromForm = formData.get('next');
    const nextSafe = (() => {
      const v = typeof nextFromForm === 'string' ? nextFromForm : undefined;
      if (!v) return next;
      const n = v.trim();
      if (!n.startsWith('/') || n.startsWith('//')) return next;
      return n;
    })();
    // Call server route to set cookie, then redirect
    // We can't use fetch in a server action without importing, but Next allows calling internal routes
    // via a redirect-after handler approach for simplicity; use GET for now.
    redirect(
      `/api/tenant/select?tenant=${encodeURIComponent(slug)}&next=${encodeURIComponent(nextSafe)}`,
    );
  }

  return (
    <div>
      <h1>Select a tenant</h1>
      <form action={chooseTenant}>
        <input type="hidden" name="next" value={next} />
        <label htmlFor="tenant-select">Tenant</label>
        <select id="tenant-select" name="tenant" defaultValue={cookieTenant ?? ''} required>
          <option value="" disabled>
            Select…
          </option>
          {memberships.map((m) => (
            <option key={m.tenantId} value={m.tenantSlug}>
              {m.tenantSlug} — {m.role}
            </option>
          ))}
        </select>
        <button type="submit">Continue</button>
      </form>
    </div>
  );
}
