import { getServerSession } from 'next-auth';
import { authOptions } from '../../../../src/lib/auth';
import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';
import { fetchFromProxy } from '../../../../app/lib/serverFetch';
import type { FlagRole } from '../../../../src/lib/roles';
import ClientToasts from './ClientToasts';
import AutoSubmitCheckbox from './AutoSubmitCheckbox';

type LegacyRole = 'Owner' | 'Admin' | 'Editor' | 'Viewer';
type MemberRow = {
  userId: string;
  email: string;
  role: LegacyRole;
  roles: string; // flags string from API (e.g., "TenantAdmin,Creator")
  rolesValue: number;
  joinedAt: string;
};

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

function parseRoles(flags: string): FlagRole[] {
  return flags
    .split(',')
    .map((s) => s.trim())
    .filter((s) => !!s) as FlagRole[];
}

export default async function MembersPage() {
  const session = await getServerSession(authOptions);
  if (!session?.user?.email) redirect('/login');

  const memberships =
    (
      session as unknown as {
        memberships?: { tenantId: string; tenantSlug: string; role: LegacyRole }[];
      }
    ).memberships ?? [];
  const currentTenant =
    (session as unknown as { tenant?: string }).tenant || cookies().get('selected_tenant')?.value;
  const mine = memberships.find((m) => m.tenantSlug === currentTenant);
  if (!mine) redirect('/select-tenant');
  if (mine.role !== 'Owner' && mine.role !== 'Admin') {
    // Non-admin: render a simple 403 message per acceptance
    return <div>403 — Access denied</div>;
  }

  // Fetch role-aware memberships via proxy
  const listRes = await fetchFromProxy(`/api-proxy/tenants/${mine.tenantId}/memberships`, {
    cache: 'no-store',
  });
  if (!listRes.ok) return <div>Failed to load members</div>;
  const members = (await listRes.json()) as MemberRow[];

  const tenantId = mine.tenantId;

  async function saveMemberRoles(formData: FormData) {
    'use server';
    const userId = String(formData.get('userId'));
    const admin = formData.get('TenantAdmin') === 'on';
    const approver = formData.get('Approver') === 'on';
    const creator = formData.get('Creator') === 'on';
    const learner = formData.get('Learner') === 'on';
    const roles: FlagRole[] = [];
    if (admin) roles.push('TenantAdmin');
    if (approver) roles.push('Approver');
    if (creator) roles.push('Creator');
    if (learner) roles.push('Learner');
    try {
      await fetchFromProxy(`/api-proxy/tenants/${tenantId}/memberships/${userId}/roles`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ roles }),
        cache: 'no-store',
      });
      revalidatePath('/studio/admin/members');
      redirect('/studio/admin/members?ok=roles-saved');
    } catch {
      redirect('/studio/admin/members?err=roles-failed');
    }
  }

  // Compute last-admin counts to disable unchecking last admin in UI
  const currentAdmins = members
    .filter((m) => parseRoles(m.roles).includes('TenantAdmin'))
    .map((m) => m.userId);
  const isLastAdmin = (userId: string) => currentAdmins.length === 1 && currentAdmins[0] === userId;

  return (
    <div className="mx-auto max-w-5xl p-4">
      <ClientToasts />
      <div className="mb-4">
        <h1 className="text-xl font-semibold">Members — {mine.tenantSlug}</h1>
        <p className="text-sm text-muted">Manage tenant roles. Changes apply immediately.</p>
      </div>

      <div className="overflow-x-auto rounded-md border border-[var(--color-line)] bg-[var(--color-surface-raised)]">
        <table className="w-full text-sm">
          <thead className="bg-[var(--color-surface)]">
            <tr className="text-left">
              <th className="px-3 py-2 font-medium">Email</th>
              <th className="px-3 py-2 font-medium">Admin</th>
              <th className="px-3 py-2 font-medium">Approver</th>
              <th className="px-3 py-2 font-medium">Creator</th>
              <th className="px-3 py-2 font-medium">Learner</th>
              <th className="px-3 py-2 font-medium">Joined</th>
            </tr>
          </thead>
          <tbody>
            {members.map((m) => {
              const flags = new Set(parseRoles(m.roles));
              const lastAdmin = isLastAdmin(m.userId);
              return (
                <tr key={m.userId} className="border-t border-[var(--color-line)]">
                  <td className="px-3 py-2">{m.email}</td>
                  <td className="px-3 py-2 align-top">
                    <form action={saveMemberRoles} className="inline-flex items-start gap-2">
                      <input type="hidden" name="userId" value={m.userId} />
                      <AutoSubmitCheckbox
                        name="TenantAdmin"
                        label="Admin"
                        defaultChecked={flags.has('TenantAdmin')}
                        disabled={lastAdmin}
                        describedById={lastAdmin ? `admin-help-${m.userId}` : undefined}
                      />
                    </form>
                    {lastAdmin && (
                      <p id={`admin-help-${m.userId}`} className="mt-1 text-xs text-muted">
                        You can’t remove the last TenantAdmin.
                      </p>
                    )}
                  </td>
                  <td className="px-3 py-2">
                    <form action={saveMemberRoles}>
                      <input type="hidden" name="userId" value={m.userId} />
                      <AutoSubmitCheckbox
                        name="Approver"
                        label="Approver"
                        defaultChecked={flags.has('Approver')}
                      />
                    </form>
                  </td>
                  <td className="px-3 py-2">
                    <form action={saveMemberRoles}>
                      <input type="hidden" name="userId" value={m.userId} />
                      <AutoSubmitCheckbox
                        name="Creator"
                        label="Creator"
                        defaultChecked={flags.has('Creator')}
                      />
                    </form>
                  </td>
                  <td className="px-3 py-2">
                    <form action={saveMemberRoles}>
                      <input type="hidden" name="userId" value={m.userId} />
                      <AutoSubmitCheckbox
                        name="Learner"
                        label="Learner"
                        defaultChecked={
                          flags.has('Learner') ||
                          (!flags.has('TenantAdmin') &&
                            !flags.has('Approver') &&
                            !flags.has('Creator'))
                        }
                      />
                    </form>
                  </td>
                  <td className="px-3 py-2 text-muted">{new Date(m.joinedAt).toLocaleString()}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
