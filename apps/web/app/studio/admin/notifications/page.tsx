import { getServerSession } from 'next-auth';
import { authOptions } from '../../../../src/lib/auth';
import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';
import { fetchFromProxy } from '../../../lib/serverFetch';
import styles from './styles.module.css';

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

// Minimal DTOs to render the list
interface DlqItem {
  id: string;
  kind: string;
  toEmail: string;
  status: string;
  attemptCount: number;
  createdAt: string;
  updatedAt?: string;
  lastError?: string | null;
}

export default async function NotificationsDlqPage() {
  const session = await getServerSession(authOptions);
  if (!session?.user?.email) redirect('/login');

  const memberships =
    (
      session as unknown as {
        memberships?: { tenantSlug: string; tenantId: string; role: string }[];
      }
    ).memberships ?? [];
  const currentTenant =
    (session as unknown as { tenant?: string }).tenant || cookies().get('selected_tenant')?.value;
  const mine = memberships.find((m) => m.tenantSlug === currentTenant);
  if (!mine) redirect('/select-tenant');
  if (mine.role !== 'Owner' && mine.role !== 'Admin') redirect('/');

  // Fetch first page of DLQ
  const resp = await fetchFromProxy(`/api-proxy/notifications/dlq?take=50`);
  if (!resp.ok) {
    return <div>Failed to load DLQ</div>;
  }
  const total = Number(resp.headers.get('x-total-count') || '0');
  const items = (await resp.json()) as DlqItem[];

  async function replaySelected(formData: FormData) {
    'use server';
    const ids = String(formData.get('ids') ?? '')
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean);
    if (ids.length === 0) return;
    await fetchFromProxy('/api-proxy/notifications/dlq', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ids }),
    });
    revalidatePath('/studio/admin/notifications');
  }

  async function replayAllFiltered() {
    'use server';
    // Here we just ask API to take up to 50 most recent
    await fetchFromProxy('/api-proxy/notifications/dlq', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ limit: 50 }),
    });
    revalidatePath('/studio/admin/notifications');
  }

  return (
    <div>
      <h1>Notifications DLQ</h1>
      <p>
        Tenant: <strong>{mine.tenantSlug}</strong> — Total: {total}
      </p>
      <form action={replaySelected} className={styles.actionsForm}>
        <input
          type="text"
          name="ids"
          placeholder="comma-separated ids"
          className={styles.idsInput}
        />
        <button type="submit">Replay selected</button>
      </form>
      <form action={replayAllFiltered}>
        <button type="submit">Replay up to 50 newest</button>
      </form>

      <table className={styles.table}>
        <thead>
          <tr>
            <th align="left">Id</th>
            <th align="left">Kind</th>
            <th align="left">To</th>
            <th align="left">Status</th>
            <th align="right">Attempts</th>
            <th align="left">Created</th>
            <th align="left">Error</th>
          </tr>
        </thead>
        <tbody>
          {items.map((n) => (
            <tr key={n.id} className={styles.row}>
              <td className={styles.mono}>{n.id}</td>
              <td>{n.kind}</td>
              <td>{n.toEmail}</td>
              <td>{n.status}</td>
              <td align="right">{n.attemptCount}</td>
              <td>{new Date(n.createdAt).toLocaleString()}</td>
              <td className={styles.errorCell} title={n.lastError ?? ''}>
                {n.lastError ?? ''}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
