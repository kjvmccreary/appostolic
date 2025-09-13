import { getServerSession } from 'next-auth';
import { authOptions } from '../../../../src/lib/auth';
import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';

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
    </div>
  );
}
