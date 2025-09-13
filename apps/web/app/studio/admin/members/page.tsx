import { getServerSession } from 'next-auth';
import { authOptions } from '../../../../src/lib/auth';
import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';

type Membership = { tenantId: string; tenantSlug: string; role: string };

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

export default async function MembersPage() {
  const session = await getServerSession(authOptions);
  if (!session?.user?.email) redirect('/login');

  const memberships = (session as unknown as { memberships?: Membership[] }).memberships ?? [];
  const currentTenant =
    (session as unknown as { tenant?: string }).tenant || cookies().get('selected_tenant')?.value;
  const mine = memberships.find((m) => m.tenantSlug === currentTenant);
  if (!mine) redirect('/select-tenant');
  if (mine.role !== 'Owner' && mine.role !== 'Admin') redirect('/');

  // Fetch members via proxy
  const res = await fetch(`/api-proxy/tenants/${mine.tenantId}/members`, { cache: 'no-store' });
  if (!res.ok) {
    return <div>Failed to load members</div>;
  }
  const members = (await res.json()) as {
    userId: string;
    email: string;
    role: string;
    joinedAt: string;
  }[];

  // Fetch invites
  const invitesRes = await fetch(`/api-proxy/tenants/${mine.tenantId}/invites`, {
    cache: 'no-store',
  });
  const invites = invitesRes.ok
    ? ((await invitesRes.json()) as {
        email: string;
        role: string;
        expiresAt: string;
        acceptedAt?: string | null;
        invitedByEmail?: string | null;
        acceptedByEmail?: string | null;
      }[])
    : [];

  const tenantId = mine.tenantId;

  async function createInvite(formData: FormData) {
    'use server';
    const email = String(formData.get('email') ?? '').trim();
    const role = String(formData.get('role') ?? 'Viewer');
    if (!email) return;
    await fetch(`/api-proxy/tenants/${tenantId}/invites`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, role }),
      cache: 'no-store',
    });
    revalidatePath('/studio/admin/members');
  }

  async function resendInvite(formData: FormData) {
    'use server';
    const email = String(formData.get('email'));
    await fetch(`/api-proxy/tenants/${tenantId}/invites/${encodeURIComponent(email)}`, {
      method: 'POST',
      cache: 'no-store',
    });
    revalidatePath('/studio/admin/members');
  }

  async function revokeInvite(formData: FormData) {
    'use server';
    const email = String(formData.get('email'));
    await fetch(`/api-proxy/tenants/${tenantId}/invites/${encodeURIComponent(email)}`, {
      method: 'DELETE',
      cache: 'no-store',
    });
    revalidatePath('/studio/admin/members');
  }
  return (
    <div>
      <h1>Members — {mine.tenantSlug}</h1>
      <table>
        <thead>
          <tr>
            <th>Email</th>
            <th>Role</th>
            <th>Joined</th>
          </tr>
        </thead>
        <tbody>
          {members.map((m) => (
            <tr key={m.userId}>
              <td>{m.email}</td>
              <td>{m.role}</td>
              <td>{new Date(m.joinedAt).toLocaleString()}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <h2>Invite member</h2>
      <form action={createInvite}>
        <input name="email" type="email" placeholder="email@example.com" required />
        <select name="role" defaultValue="Viewer" aria-label="Role">
          <option value="Viewer">Viewer</option>
          <option value="Editor">Editor</option>
          <option value="Admin">Admin</option>
        </select>
        <button type="submit">Send invite</button>
      </form>

      <h2>Invites</h2>
      <table>
        <thead>
          <tr>
            <th>Email</th>
            <th>Role</th>
            <th>Expires</th>
            <th>Status</th>
            <th>Invited By</th>
            <th>Accepted By</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {invites.map((i) => (
            <tr key={i.email}>
              <td>{i.email}</td>
              <td>{i.role}</td>
              <td>{new Date(i.expiresAt).toLocaleString()}</td>
              <td>
                {i.acceptedAt ? `Accepted ${new Date(i.acceptedAt).toLocaleString()}` : 'Pending'}
              </td>
              <td>{i.invitedByEmail ?? ''}</td>
              <td>{i.acceptedByEmail ?? ''}</td>
              <td>
                {!i.acceptedAt && (
                  <span>
                    <form action={resendInvite}>
                      <input type="hidden" name="email" value={i.email} />
                      <button type="submit">Resend</button>
                    </form>
                    <form action={revokeInvite}>
                      <input type="hidden" name="email" value={i.email} />
                      <button type="submit">Revoke</button>
                    </form>
                  </span>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
