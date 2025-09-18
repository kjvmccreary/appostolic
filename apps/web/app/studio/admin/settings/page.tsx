import { getServerSession } from 'next-auth';
import { authOptions } from '../../../../src/lib/auth';
import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { computeBooleansForTenant } from '../../../../src/lib/roles';

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

export default async function TenantSettingsPage() {
  const session = await getServerSession(authOptions);
  if (!session?.user?.email) redirect('/login');

  const tenantClaim = (session as unknown as { tenant?: string }).tenant;
  const cookieTenant = cookies().get('selected_tenant')?.value;
  const memberships =
    (
      session as unknown as {
        memberships?: { tenantId: string; tenantSlug: string; role: string; roles?: string[] }[];
      }
    ).memberships || [];

  // Determine the effective tenant slug: prefer JWT claim; fall back to cookie; if either is a tenantId, resolve to slug.
  const rawSel = tenantClaim || cookieTenant || '';
  const match = memberships.find((m) => m.tenantSlug === rawSel || m.tenantId === rawSel) || null;
  if (!match) redirect('/select-tenant');
  const effectiveSlug = match.tenantSlug;

  // Use shared roles helper to compute admin based on selected tenant membership (accepts flags and legacy roles, including Owner).
  const { isAdmin } = computeBooleansForTenant(
    memberships as unknown as {
      tenantId: string;
      tenantSlug: string;
      role: 'Owner' | 'Admin' | 'Editor' | 'Viewer';
      roles?: Array<'TenantAdmin' | 'Approver' | 'Creator' | 'Learner' | string>;
    }[],
    effectiveSlug,
  );
  if (!isAdmin) return <div>403 — Access denied</div>;

  return (
    <div className="mx-auto max-w-3xl p-4">
      <h1 className="text-xl font-semibold">Tenant Settings — {String(effectiveSlug)}</h1>
      <p className="mt-2 text-sm text-muted">Basic settings for this tenant will appear here.</p>
      <div className="mt-4 rounded-md border border-[var(--color-line)] bg-[var(--color-surface-raised)] p-3">
        <p className="text-sm">This is a placeholder page. Coming soon.</p>
      </div>
    </div>
  );
}
