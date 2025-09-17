import { getServerSession } from 'next-auth';
import { authOptions } from '../../../../src/lib/auth';
import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

export default async function TenantSettingsPage() {
  const session = await getServerSession(authOptions);
  if (!session?.user?.email) redirect('/login');

  const selectedTenant =
    (session as unknown as { tenant?: string }).tenant || cookies().get('selected_tenant')?.value;
  const memberships =
    (
      session as unknown as {
        memberships?: { tenantId: string; tenantSlug: string; role?: string; roles?: string[] }[];
      }
    ).memberships || [];
  const mine = memberships.find(
    (m) => m.tenantSlug === selectedTenant || m.tenantId === selectedTenant,
  );
  if (!mine) redirect('/select-tenant');

  const roles = [mine.role, ...(Array.isArray(mine.roles) ? mine.roles : [])]
    .filter(Boolean)
    .map((r) => String(r).toLowerCase());
  const isAdmin = roles.includes('admin') || roles.includes('tenantadmin');
  if (!isAdmin) return <div>403 — Access denied</div>;

  return (
    <div className="mx-auto max-w-3xl p-4">
      <h1 className="text-xl font-semibold">Tenant Settings — {String(selectedTenant)}</h1>
      <p className="mt-2 text-sm text-muted">Basic settings for this tenant will appear here.</p>
      <div className="mt-4 rounded-md border border-[var(--color-line)] bg-[var(--color-surface-raised)] p-3">
        <p className="text-sm">This is a placeholder page. Coming soon.</p>
      </div>
    </div>
  );
}
