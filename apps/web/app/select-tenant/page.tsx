import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { getServerSession } from 'next-auth';
import { authOptions } from '../../src/lib/auth';
import type { Session } from 'next-auth';

const TENANT_COOKIE = 'selected_tenant';

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

export default async function SelectTenantPage(props: {
  searchParams: Record<string, string | string[] | undefined>;
}) {
  const searchParams = props.searchParams ?? {};
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
    const tenant = memberships[0].tenantSlug;
    // Write cookie directly (canonical slug) and redirect to next.
    const isSecure = process.env.NODE_ENV === 'production';
    c.set(TENANT_COOKIE, tenant, {
      path: '/',
      httpOnly: true,
      sameSite: 'lax',
      maxAge: 60 * 60 * 24 * 7,
      secure: isSecure,
    });
    redirect(next);
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
    const session = await getServerSession(authOptions);
    const membership = (session as Session & { memberships?: Membership[] })?.memberships?.find(
      (m) => m.tenantSlug === slug || m.tenantId === slug,
    );
    if (!membership) {
      redirect('/select-tenant'); // invalid selection attempt; start over
    }
    const isSecure = process.env.NODE_ENV === 'production';
    cookies().set(TENANT_COOKIE, membership.tenantSlug, {
      path: '/',
      httpOnly: true,
      sameSite: 'lax',
      maxAge: 60 * 60 * 24 * 7,
      secure: isSecure,
    });
    redirect(nextSafe);
  }

  return (
    <div aria-labelledby="select-tenant-heading" className="mx-auto mt-8 max-w-md p-4">
      <h1 id="select-tenant-heading" className="mb-4 text-xl font-semibold">
        Select a tenant
      </h1>
      <form action={chooseTenant}>
        <input type="hidden" name="next" value={next} />
        <label htmlFor="tenant-select" className="block text-sm mb-1">
          Tenant
        </label>
        <select
          id="tenant-select"
          name="tenant"
          defaultValue={cookieTenant ?? ''}
          required
          className="mb-3 w-full rounded border border-line bg-[var(--color-surface)] p-2 text-sm focus-ring"
        >
          <option value="" disabled>
            Select…
          </option>
          {memberships.map((m) => (
            <option key={m.tenantId} value={m.tenantSlug}>
              {m.tenantSlug} — {m.role}
            </option>
          ))}
        </select>
        <button type="submit" className="inline-flex rounded border border-line px-3 py-1">
          Continue
        </button>
      </form>
    </div>
  );
}
