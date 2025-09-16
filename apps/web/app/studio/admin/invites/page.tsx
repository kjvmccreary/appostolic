import { getServerSession } from 'next-auth';
import { authOptions } from '../../../../src/lib/auth';
import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { fetchFromProxy } from '../../../lib/serverFetch';
import ConfirmSubmitButton from '../../../../src/components/ui/ConfirmSubmitButton';
import ClientToasts from './ClientToasts';
import EmailField from './EmailField';
import type { FlagRole } from '../../../../src/lib/roles';

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

type Invite = {
  email: string;
  role: string;
  roles?: string;
  rolesValue?: number;
  expiresAt: string;
  invitedByEmail?: string | null;
  acceptedByEmail?: string | null;
};

export default async function InvitesAdminPage() {
  const session = await getServerSession(authOptions);
  if (!session?.user?.email) {
    redirect('/login');
    return null;
  }

  const memberships =
    (
      session as unknown as {
        memberships?: { tenantSlug: string; tenantId: string; role: string }[];
      }
    ).memberships ?? [];
  const currentTenant =
    (session as unknown as { tenant?: string })?.tenant || cookies().get('selected_tenant')?.value;
  const mine = memberships.find((m) => m.tenantSlug === currentTenant);
  if (!mine) {
    redirect('/select-tenant');
    return null;
  }
  if (mine.role !== 'Owner' && mine.role !== 'Admin') {
    return <div>403 — Access denied</div>;
  }

  // Capture for server actions (TypeScript can't narrow across closures)
  const tenantId = mine.tenantId;
  const tenantSlug = mine.tenantSlug;

  async function createInvite(formData: FormData) {
    'use server';
    const email = String(formData.get('email') ?? '').trim();
    // role is a single selected flag role value (uses FlagRole values for correctness)
    const selected = String(formData.get('role') ?? 'Learner') as FlagRole;
    if (!email) return;
    try {
      await fetchFromProxy(`/api-proxy/tenants/${tenantId}/invites`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        // Send as roles flags array to align with IAM 2.2 invitations.roles
        body: JSON.stringify({ email, roles: [selected] }),
      });
      redirect('/studio/admin/invites?ok=invite-created');
    } catch {
      redirect('/studio/admin/invites?err=invite-failed');
    }
  }

  async function resendInvite(formData: FormData) {
    'use server';
    const email = String(formData.get('email') ?? '').trim();
    if (!email) return;
    try {
      await fetchFromProxy(
        `/api-proxy/tenants/${tenantId}/invites/${encodeURIComponent(email)}/resend`,
        { method: 'POST' },
      );
      redirect('/studio/admin/invites?ok=invite-resent');
    } catch {
      redirect('/studio/admin/invites?err=invite-resend-failed');
    }
  }

  async function revokeInvite(formData: FormData) {
    'use server';
    const email = String(formData.get('email') ?? '').trim();
    if (!email) return;
    try {
      await fetchFromProxy(`/api-proxy/tenants/${tenantId}/invites/${encodeURIComponent(email)}`, {
        method: 'DELETE',
      });
      redirect('/studio/admin/invites?ok=invite-revoked');
    } catch {
      redirect('/studio/admin/invites?err=invite-revoke-failed');
    }
  }

  const res = await fetchFromProxy(`/api-proxy/tenants/${tenantId}/invites`, {
    method: 'GET',
    cache: 'no-store',
  });
  if (!res.ok) {
    return <div>Failed to load invites</div>;
  }
  const invites = (await res.json()) as Invite[];

  // ok/err handled by client-side toasts; keep SSR stable

  return (
    <div className="mx-auto max-w-3xl p-4">
      <h1 className="text-xl font-semibold mb-3">Invites</h1>
      <p className="text-sm text-muted mb-4">Tenant: {tenantSlug}</p>

      <ClientToasts />

      <form action={createInvite} className="mb-4 flex gap-2 items-start">
        <EmailField name="email" placeholder="email@example.com" />
        <label htmlFor="invite-role" className="text-sm text-muted sr-only">
          Role
        </label>
        <select
          id="invite-role"
          aria-label="Role"
          name="role"
          defaultValue="Learner"
          className="h-8 rounded-md border border-line bg-[var(--color-surface-raised)] px-2 text-sm"
        >
          {/* Use flag values for option values; label 'Admin' maps to 'TenantAdmin' */}
          <option value="TenantAdmin">Admin</option>
          <option value="Approver">Approver</option>
          <option value="Creator">Creator</option>
          <option value="Learner">Learner</option>
        </select>
        <button
          type="submit"
          className="h-8 rounded-md bg-[var(--color-accent-600)] px-3 text-sm text-white"
        >
          Invite
        </button>
      </form>

      {invites.length === 0 ? (
        <div className="rounded-md border border-dashed border-line p-4 text-sm text-muted">
          No pending invites. Use the form above to invite someone to this tenant.
        </div>
      ) : (
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="text-left border-b border-line">
              <th className="py-2 pr-2">Email</th>
              <th className="py-2 pr-2">Role</th>
              <th className="py-2 pr-2">Invited By</th>
              <th className="py-2 pr-2">Accepted By</th>
              <th className="py-2 pr-2">Expires</th>
              <th className="py-2 pr-2">Actions</th>
            </tr>
          </thead>
          <tbody>
            {invites.map((i, idx) => (
              <tr key={i.email} className="border-b border-line/50">
                <td className="py-2 pr-2">{i.email}</td>
                <td className="py-2 pr-2">{i.role}</td>
                <td className="py-2 pr-2">{i.invitedByEmail ?? '—'}</td>
                <td className="py-2 pr-2">{i.acceptedByEmail ?? '—'}</td>
                <td className="py-2 pr-2">{new Date(i.expiresAt).toLocaleString()}</td>
                <td className="py-2 pr-2">
                  <form action={resendInvite} className="inline">
                    <input type="hidden" name="email" value={i.email} />
                    <button type="submit" className="rounded border px-2 py-1 mr-2">
                      Resend
                    </button>
                  </form>
                  <form id={`revoke-form-${idx}`} action={revokeInvite} className="inline">
                    <input type="hidden" name="email" value={i.email} />
                    {/* The submit is triggered via a client-side confirm button for safety */}
                  </form>
                  <ConfirmSubmitButton
                    formId={`revoke-form-${idx}`}
                    label="Revoke"
                    confirmText={`Revoke invite for ${i.email}?`}
                    className="rounded border px-2 py-1 text-red-600"
                  />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
