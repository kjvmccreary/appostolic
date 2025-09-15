import { getServerSession } from 'next-auth';
import { authOptions } from '../../../../src/lib/auth';
import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';
import { fetchFromProxy } from '../../../../app/lib/serverFetch';
import type { FlagRole } from '../../../../src/lib/roles';

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
    await fetchFromProxy(`/api-proxy/tenants/${tenantId}/memberships/${userId}/roles`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ roles }),
      cache: 'no-store',
    });
    revalidatePath('/studio/admin/members');
  }

  // Compute last-admin counts to disable unchecking last admin in UI
  const currentAdmins = members
    .filter((m) => parseRoles(m.roles).includes('TenantAdmin'))
    .map((m) => m.userId);
  const isLastAdmin = (userId: string) => currentAdmins.length === 1 && currentAdmins[0] === userId;

  return (
    <div>
      <h1>Members — {mine.tenantSlug}</h1>
      <table>
        <thead>
          <tr>
            <th>Email</th>
            <th>Admin</th>
            <th>Approver</th>
            <th>Creator</th>
            <th>Learner</th>
            <th>Joined</th>
          </tr>
        </thead>
        <tbody>
          {members.map((m) => {
            const flags = new Set(parseRoles(m.roles));
            const lastAdmin = isLastAdmin(m.userId);
            return (
              <tr key={m.userId}>
                <td>{m.email}</td>
                <td>
                  <form action={saveMemberRoles}>
                    <input type="hidden" name="userId" value={m.userId} />
                    <label>
                      <input
                        aria-label="Admin"
                        type="checkbox"
                        name="TenantAdmin"
                        defaultChecked={flags.has('TenantAdmin')}
                        disabled={lastAdmin}
                      />
                    </label>
                  </form>
                </td>
                <td>
                  <form action={saveMemberRoles}>
                    <input type="hidden" name="userId" value={m.userId} />
                    <label>
                      <input
                        aria-label="Approver"
                        type="checkbox"
                        name="Approver"
                        defaultChecked={flags.has('Approver')}
                      />
                    </label>
                  </form>
                </td>
                <td>
                  <form action={saveMemberRoles}>
                    <input type="hidden" name="userId" value={m.userId} />
                    <label>
                      <input
                        aria-label="Creator"
                        type="checkbox"
                        name="Creator"
                        defaultChecked={flags.has('Creator')}
                      />
                    </label>
                  </form>
                </td>
                <td>
                  <form action={saveMemberRoles}>
                    <input type="hidden" name="userId" value={m.userId} />
                    <label>
                      <input
                        aria-label="Learner"
                        type="checkbox"
                        name="Learner"
                        defaultChecked={
                          flags.has('Learner') ||
                          (!flags.has('TenantAdmin') &&
                            !flags.has('Approver') &&
                            !flags.has('Creator'))
                        }
                      />
                    </label>
                  </form>
                </td>
                <td>{new Date(m.joinedAt).toLocaleString()}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
