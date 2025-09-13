import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { getServerSession } from 'next-auth';
import { authOptions } from '../../src/lib/auth';
import type { Session } from 'next-auth';

const TENANT_COOKIE = 'selected_tenant';

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

export default async function SelectTenantPage() {
  const session = await getServerSession(authOptions);
  if (!session?.user?.email) {
    redirect('/login');
  }
  type Membership = { tenantId: string; tenantSlug: string; role: string };
  const memberships = (session as Session & { memberships?: Membership[] }).memberships ?? [];
  const c = cookies();
  const cookieTenant = c.get(TENANT_COOKIE)?.value;

  if (memberships && memberships.length === 1) {
    // Auto-select single membership
    c.set(TENANT_COOKIE, memberships[0].tenantSlug, { httpOnly: true, sameSite: 'lax', path: '/' });
    redirect('/studio');
  }

  async function chooseTenant(formData: FormData) {
    'use server';
    const slug = String(formData.get('tenant'));
    const cookieStore = cookies();
    cookieStore.set(TENANT_COOKIE, slug, { httpOnly: true, sameSite: 'lax', path: '/' });
    redirect('/studio');
  }

  return (
    <div>
      <h1>Select a tenant</h1>
      <form action={chooseTenant}>
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
