import { getServerSession } from 'next-auth';
import { authOptions } from '../../../../src/lib/auth';
import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';
import { fetchFromProxy } from '../../../lib/serverFetch';
import styles from './styles.module.css';
import ClientToasts from './ClientToasts';
import ConfirmSubmitButton from '../../../../src/components/ui/ConfirmSubmitButton';

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

export default async function NotificationsDlqPage(props: {
  searchParams?: Record<string, string | string[] | undefined>;
}) {
  const { searchParams } = props || {};
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
  // Enforce admin in UI: render 403 message rather than redirecting.
  if (mine.role !== 'Owner' && mine.role !== 'Admin') {
    return <div>403 — Access denied</div>;
  }

  // Parse filters/paging from URL
  const take = Math.min(Math.max(Number(searchParams?.take ?? '50') || 50, 1), 200);
  const skip = Math.max(Number(searchParams?.skip ?? '0') || 0, 0);
  const status =
    (typeof searchParams?.status === 'string' ? searchParams?.status : undefined) ?? '';
  const kind = (typeof searchParams?.kind === 'string' ? searchParams?.kind : undefined) ?? '';

  const qs = new URLSearchParams();
  qs.set('take', String(take));
  if (skip) qs.set('skip', String(skip));
  if (status && status !== 'all') qs.set('status', status);
  if (kind) qs.set('kind', kind);

  // Fetch filtered/paged DLQ
  const resp = await fetchFromProxy(`/api-proxy/notifications/dlq?${qs.toString()}`);
  if (!resp.ok) {
    return <div>Failed to load DLQ</div>;
  }
  const total = Number(resp.headers.get('x-total-count') || '0');
  const items = (await resp.json()) as DlqItem[];

  // Build suggestions for kinds from current page
  const kindSuggestions = Array.from(new Set(items.map((i) => i.kind))).sort();

  async function replaySelected(formData: FormData) {
    'use server';
    const ids = String(formData.get('ids') ?? '')
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean);
    if (ids.length === 0) {
      redirect('/studio/admin/notifications?err=Enter one or more ids');
    }
    try {
      await fetchFromProxy('/api-proxy/notifications/dlq', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ids }),
      });
      revalidatePath('/studio/admin/notifications');
      redirect('/studio/admin/notifications?ok=Requeued selected');
    } catch {
      redirect('/studio/admin/notifications?err=Replay failed');
    }
  }

  async function replayAllFiltered() {
    'use server';
    // Ask API to replay using current filters, limiting by current page size
    const body: Record<string, unknown> = { limit: take };
    if (status && status !== 'all') body.status = status;
    if (kind) body.kind = kind;
    try {
      await fetchFromProxy('/api-proxy/notifications/dlq', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      revalidatePath('/studio/admin/notifications');
      redirect('/studio/admin/notifications?ok=Requeued filtered');
    } catch {
      redirect('/studio/admin/notifications?err=Replay failed');
    }
  }

  async function replayOne(formData: FormData) {
    'use server';
    const id = String(formData.get('id') ?? '').trim();
    if (!id) return;
    try {
      await fetchFromProxy('/api-proxy/notifications/dlq', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ids: [id] }),
      });
      revalidatePath('/studio/admin/notifications');
      redirect('/studio/admin/notifications?ok=Requeued 1');
    } catch {
      redirect('/studio/admin/notifications?err=Replay failed');
    }
  }

  // Helpers for pagination links
  const baseParams = new URLSearchParams();
  baseParams.set('take', String(take));
  if (status) baseParams.set('status', status);
  if (kind) baseParams.set('kind', kind);
  const showingFrom = total === 0 ? 0 : skip + 1;
  const showingTo = Math.min(skip + items.length, total);
  const hasPrev = skip > 0;
  const hasNext = skip + items.length < total;
  const prevParams = new URLSearchParams(baseParams);
  const nextParams = new URLSearchParams(baseParams);
  prevParams.set('skip', String(Math.max(skip - take, 0)));
  nextParams.set('skip', String(skip + take));

  return (
    <div>
      <ClientToasts />
      <h1>Notifications DLQ</h1>
      <p>
        Tenant: <strong>{mine.tenantSlug}</strong> — Total: {total}
      </p>

      <form method="GET" className={styles.filtersForm}>
        <label>
          Status:{' '}
          <select name="status" defaultValue={status || 'all'}>
            <option value="all">All</option>
            <option value="Failed">Failed</option>
            <option value="DeadLetter">DeadLetter</option>
          </select>
        </label>
        <label>
          Kind:{' '}
          <input
            name="kind"
            defaultValue={kind}
            list="kind-suggestions"
            placeholder="e.g. Verification"
          />
          <datalist id="kind-suggestions">
            {kindSuggestions.map((k) => (
              <option key={k} value={k} />
            ))}
          </datalist>
        </label>
        <label>
          Page size:{' '}
          <select name="take" defaultValue={String(take)}>
            <option value="10">10</option>
            <option value="25">25</option>
            <option value="50">50</option>
            <option value="100">100</option>
          </select>
        </label>
        <input type="hidden" name="skip" value={String(Math.min(skip, Math.max(total - 1, 0)))} />
        <button type="submit">Apply</button>
      </form>

      <form action={replaySelected} className={styles.actionsForm}>
        <input
          type="text"
          name="ids"
          placeholder="comma-separated ids"
          className={styles.idsInput}
        />
        <button type="submit">Replay selected</button>
      </form>
      <form id="replay-all" action={replayAllFiltered}>
        <ConfirmSubmitButton
          formId="replay-all"
          label={`Replay up to ${take} newest (filtered)`}
          confirmText={`Requeue up to ${take} notifications matching current filters?`}
          className="inline-block"
        />
      </form>

      <div className={styles.pager}>
        <span>
          Showing {showingFrom}-{showingTo} of {total}
        </span>
        <span className={styles.pagerButtons}>
          {hasPrev ? (
            <a href={`/studio/admin/notifications?${prevParams.toString()}`}>◀ Prev</a>
          ) : (
            <span className={styles.disabledLink}>◀ Prev</span>
          )}
          {hasNext ? (
            <a href={`/studio/admin/notifications?${nextParams.toString()}`}>Next ▶</a>
          ) : (
            <span className={styles.disabledLink}>Next ▶</span>
          )}
        </span>
      </div>

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
            <th align="left">Actions</th>
          </tr>
        </thead>
        <tbody>
          {items.length === 0 ? (
            <tr>
              <td colSpan={8} className={styles.errorCell}>
                No items found for the selected filters.
              </td>
            </tr>
          ) : (
            items.map((n) => (
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
                <td>
                  <form id={`replay-${n.id}`} action={replayOne}>
                    <input type="hidden" name="id" value={n.id} />
                    <ConfirmSubmitButton
                      formId={`replay-${n.id}`}
                      label="Replay"
                      confirmText="Requeue this notification?"
                      className="inline-block"
                    />
                  </form>
                </td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}
