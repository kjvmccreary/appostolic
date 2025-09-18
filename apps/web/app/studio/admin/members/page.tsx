import { getServerSession } from 'next-auth';
import { authOptions } from '../../../../src/lib/auth';
import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';
import { fetchFromProxy } from '../../../../app/lib/serverFetch';
import {
  computeBooleansForTenant,
  type Membership as RolesMembership,
  type FlagRole,
} from '../../../../src/lib/roles';
import ClientToasts from './ClientToasts';
import AutoSubmitCheckbox from './AutoSubmitCheckbox';

type LegacyRole = 'Owner' | 'Admin' | 'Editor' | 'Viewer';
type MemberRow = {
  userId: string;
  email: string;
  role: LegacyRole;
  roles: string; // flags string from API (e.g., "TenantAdmin,Creator")
  rolesValue: number;
  status: 'Active' | 'Invited' | 'Suspended' | 'Revoked';
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
        memberships?: RolesMembership[];
      }
    ).memberships ?? [];
  const currentTenant =
    (session as unknown as { tenant?: string }).tenant || cookies().get('selected_tenant')?.value;
  const mine = memberships.find((m) => m.tenantSlug === currentTenant);
  if (!mine) redirect('/select-tenant');
  // Flags-based gating to match proxy guard logic
  const { isAdmin } = computeBooleansForTenant(memberships as RolesMembership[], mine.tenantSlug);
  if (!isAdmin) return <div>403 — Access denied</div>;

  // Fetch role-aware memberships via proxy
  const listRes = await fetchFromProxy(`/api-proxy/tenants/${mine.tenantId}/memberships`, {
    cache: 'no-store',
  });
  if (listRes.status === 401) redirect('/select-tenant');
  if (listRes.status === 403) return <div>403 — Access denied</div>;
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
    // Call proxy and make a decision based on the response, ensuring we don't
    // place the success redirect inside a try/catch. Next.js implements
    // redirect() by throwing a special error; catching that would falsely
    // route us to the error path and surface an error toast after a 303.
    try {
      const res = await fetchFromProxy(
        `/api-proxy/tenants/${tenantId}/memberships/${userId}/roles`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ roles }),
          cache: 'no-store',
        },
      );
      if (!res.ok) {
        // Backend/body validation failed or API returned non-2xx
        redirect('/studio/admin/members?err=roles-failed');
      }
    } catch {
      // Network or unexpected error
      redirect('/studio/admin/members?err=roles-failed');
    }
    // Success: revalidate and redirect with ok outside of try/catch so it's not swallowed
    revalidatePath('/studio/admin/members');
    redirect('/studio/admin/members?ok=roles-saved');
  }

  async function saveMemberStatus(formData: FormData) {
    'use server';
    const userId = String(formData.get('userId'));
    const active = formData.get('Active') === 'on';
    try {
      const res = await fetchFromProxy(`/api-proxy/tenants/${tenantId}/members/${userId}/status`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ active }),
        cache: 'no-store',
      });
      if (!res.ok) redirect('/studio/admin/members?err=status-failed');
    } catch {
      redirect('/studio/admin/members?err=status-failed');
    }
    // Revalidate so status reflects immediately
    revalidatePath('/studio/admin/members');
    redirect('/studio/admin/members?ok=status-saved');
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
        <div className="mt-1 flex items-center justify-between gap-2">
          <p className="text-sm text-muted">Manage tenant roles. Changes apply immediately.</p>
          <a
            href="/studio/admin/invites"
            className="inline-flex items-center rounded border border-line px-2 py-1 text-sm hover:bg-[var(--color-surface-raised)]"
          >
            Invite members
          </a>
        </div>
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
              <th className="px-3 py-2 font-medium">Active</th>
              <th className="px-3 py-2 font-medium">Joined</th>
            </tr>
          </thead>
          <tbody>
            {members.map((m) => {
              const flags = new Set(parseRoles(m.roles));
              const lastAdmin = isLastAdmin(m.userId);
              const formId = `roles-form-${m.userId}`;
              return (
                <tr key={m.userId} className="border-t border-[var(--color-line)]">
                  <td className="px-3 py-2">{m.email}</td>
                  <td className="px-3 py-2 align-top">
                    {/* One form per member row; all checkboxes submit together */}
                    <form
                      id={formId}
                      action={saveMemberRoles}
                      className="inline-flex items-start gap-2"
                    >
                      <input type="hidden" name="userId" value={m.userId} />
                      {/* If this is the last remaining admin, the checkbox is disabled.
                          Disabled inputs aren't submitted, so include a hidden field to
                          ensure we preserve the TenantAdmin flag on submit. */}
                      {lastAdmin ? <input type="hidden" name="TenantAdmin" value="on" /> : null}
                      <AutoSubmitCheckbox
                        name="TenantAdmin"
                        label="Admin"
                        defaultChecked={flags.has('TenantAdmin')}
                        disabled={lastAdmin}
                        describedById={lastAdmin ? `admin-help-${m.userId}` : undefined}
                        formId={formId}
                      />
                    </form>
                    {lastAdmin && (
                      <p id={`admin-help-${m.userId}`} className="mt-1 text-xs text-muted">
                        You can’t remove the last TenantAdmin.
                      </p>
                    )}
                  </td>
                  <td className="px-3 py-2">
                    <AutoSubmitCheckbox
                      name="Approver"
                      label="Approver"
                      defaultChecked={flags.has('Approver')}
                      formId={formId}
                    />
                  </td>
                  <td className="px-3 py-2">
                    <AutoSubmitCheckbox
                      name="Creator"
                      label="Creator"
                      defaultChecked={flags.has('Creator')}
                      formId={formId}
                    />
                  </td>
                  <td className="px-3 py-2">
                    <AutoSubmitCheckbox
                      name="Learner"
                      label="Learner"
                      defaultChecked={
                        flags.has('Learner') ||
                        (!flags.has('TenantAdmin') &&
                          !flags.has('Approver') &&
                          !flags.has('Creator'))
                      }
                      formId={formId}
                    />
                  </td>
                  <td className="px-3 py-2">
                    <form action={saveMemberStatus} className="inline-flex items-center gap-2">
                      <input type="hidden" name="userId" value={m.userId} />
                      {/* Disallow deactivating the last TenantAdmin */}
                      <AutoSubmitCheckbox
                        name="Active"
                        label="Active"
                        defaultChecked={m.status === 'Active'}
                        disabled={lastAdmin && m.status === 'Active'}
                      />
                    </form>
                    {lastAdmin && m.status === 'Active' && (
                      <p className="mt-1 text-xs text-muted">
                        You can’t deactivate the last TenantAdmin.
                      </p>
                    )}
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
