import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { getServerSession } from 'next-auth';
import { authOptions } from '../../src/lib/auth';
import { getFlagRoles, type FlagRole } from '../../src/lib/roles';
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
  const sessionTenant = (session as Session & { tenant?: string })?.tenant ?? null;
  const cookieMembership = cookieTenant
    ? memberships.find((m) => m.tenantSlug === cookieTenant || m.tenantId === cookieTenant)
    : undefined;
  const canonicalCookieTenant = cookieMembership?.tenantSlug ?? null;

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

  const rawReselect =
    (Array.isArray(searchParams?.reselect) ? searchParams.reselect[0] : searchParams?.reselect) ??
    undefined;
  const wantsReselect =
    rawReselect !== undefined &&
    ['1', 'true', 'yes', 'y', 'force'].includes(String(rawReselect).toLowerCase());

  if (!wantsReselect && canonicalCookieTenant && sessionTenant === canonicalCookieTenant) {
    // When a valid selection already exists (cookie aligns with current memberships), skip the
    // selector entirely and continue to the desired destination. This prevents lingering on the
    // selection screen after a successful choice while still allowing explicit reselection via
    // the ?reselect=1 query string.
    redirect(next);
  }

  if (memberships && memberships.length === 1) {
    const tenant = memberships[0].tenantSlug;
    // Redirect through our API route to set the cookie in a Route Handler (supported by Next.js)
    // and then continue to the intended destination.
    const url = new URL(
      `/api/tenant/select?tenant=${encodeURIComponent(tenant)}&next=${encodeURIComponent(next)}`,
      'http://localhost',
    );
    redirect(url.pathname + url.search);
  }

  return (
    <div aria-labelledby="select-tenant-heading" className="mx-auto mt-8 max-w-md p-4">
      <h1 id="select-tenant-heading" className="mb-4 text-xl font-semibold">
        Select a tenant
      </h1>
      <form method="get" action="/api/tenant/select">
        <input type="hidden" name="next" value={next} />
        <label htmlFor="tenant-select" className="block text-sm mb-1">
          Tenant
        </label>
        <select
          id="tenant-select"
          name="tenant"
          defaultValue={canonicalCookieTenant ?? ''}
          required
          className="mb-3 w-full rounded border border-line bg-[var(--color-surface)] p-2 text-sm focus-ring"
        >
          <option value="" disabled>
            Select…
          </option>
          {memberships.map((m) => {
            // Derive canonical role label from flags (prefer roles[] if present),
            // falling back to legacy role mapping via getFlagRoles.
            const flags = getFlagRoles({
              tenantId: m.tenantId,
              tenantSlug: m.tenantSlug,
              role: (m.role as unknown as 'Owner' | 'Admin' | 'Editor' | 'Viewer') ?? 'Viewer',
              roles: (m as unknown as { roles?: Array<FlagRole | string> }).roles,
            } as unknown as {
              tenantId: string;
              tenantSlug: string;
              role: 'Owner' | 'Admin' | 'Editor' | 'Viewer';
              roles?: Array<FlagRole | string>;
            });
            const precedence: FlagRole[] = ['TenantAdmin', 'Approver', 'Creator', 'Learner'];
            const label = (() => {
              for (const r of precedence)
                if (flags.includes(r)) return r === 'TenantAdmin' ? 'Admin' : r;
              return 'Learner';
            })();
            return (
              <option key={m.tenantId} value={m.tenantSlug}>
                {m.tenantSlug} — {label}
              </option>
            );
          })}
        </select>
        <button type="submit" className="inline-flex rounded border border-line px-3 py-1">
          Continue
        </button>
      </form>
    </div>
  );
}
