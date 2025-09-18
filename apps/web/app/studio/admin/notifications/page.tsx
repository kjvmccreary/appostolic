import { getServerSession } from 'next-auth';
import { authOptions } from '../../../../src/lib/auth';
import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { revalidatePath } from 'next/cache';
import { fetchFromProxy } from '../../../lib/serverFetch';
import { computeBooleansForTenant } from '../../../../src/lib/roles';
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
        memberships?: { tenantSlug: string; tenantId: string; role?: string; roles?: string[] }[];
      }
    ).memberships ?? [];
  const currentTenant =
    (session as unknown as { tenant?: string }).tenant || cookies().get('selected_tenant')?.value;
  const mine = memberships.find((m) => m.tenantSlug === currentTenant);
  if (!mine) redirect('/select-tenant');
  // Flags-only admin gating (legacy role field ignored / deprecated)
  const { isAdmin } = computeBooleansForTenant(
    memberships.map((m) => ({
      tenantId: m.tenantId,
      tenantSlug: m.tenantSlug,
      // Provide a placeholder legacy role to satisfy type; ignored by flags logic.
      role: 'Viewer',
      roles: m.roles || [],
    })) as unknown as Parameters<typeof computeBooleansForTenant>[0],
    mine.tenantSlug,
  );
  if (!isAdmin) return <div>403 — Access denied</div>;

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
    return (
      <div className="mx-auto max-w-5xl p-4">
        <h1 className="text-xl font-semibold mb-2">Notifications DLQ</h1>
        <div className="rounded-md border border-[var(--color-line)] bg-red-50 p-3 text-sm text-red-900">
          Failed to load DLQ
        </div>
      </div>
    );
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
  const hasPrev = skip > 0;
  const hasNext = skip + items.length < total;
  const prevParams = new URLSearchParams(baseParams);
  const nextParams = new URLSearchParams(baseParams);
  prevParams.set('skip', String(Math.max(skip - take, 0)));
  nextParams.set('skip', String(skip + take));
  const page = Math.floor(skip / take) + 1;
  const pages = Math.max(1, Math.ceil(total / take));

  return (
    <div className="mx-auto max-w-5xl p-4">
      <ClientToasts />
      <h1 className="text-xl font-semibold mb-2">Notifications DLQ</h1>
      <p className="text-sm text-muted mb-2">
        Tenant: <strong>{mine.tenantSlug}</strong> — Total: {total}
      </p>

      <form method="GET" className={`${styles.filtersForm} flex flex-wrap gap-3 items-end mb-3`}>
        <label>
          Status:{' '}
          <select
            name="status"
            defaultValue={status || 'all'}
            className="rounded border px-2 py-1 text-sm"
          >
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
            className="rounded border px-2 py-1 text-sm"
          />
          <datalist id="kind-suggestions">
            {kindSuggestions.map((k) => (
              <option key={k} value={k} />
            ))}
          </datalist>
        </label>
        <label>
          Page size:{' '}
          <select
            name="take"
            defaultValue={String(take)}
            className="rounded border px-2 py-1 text-sm"
          >
            <option value="10">10</option>
            <option value="25">25</option>
            <option value="50">50</option>
            <option value="100">100</option>
          </select>
        </label>
        <input type="hidden" name="skip" value={String(Math.min(skip, Math.max(total - 1, 0)))} />
        <button
          type="submit"
          className="px-3 py-1 rounded bg-[var(--color-accent-600)] text-white text-sm"
        >
          Apply
        </button>
      </form>

      <form action={replaySelected} className={`${styles.actionsForm} flex items-center gap-2`}>
        <input
          type="text"
          name="ids"
          placeholder="comma-separated ids"
          className={`${styles.idsInput} rounded border px-2 py-1 text-sm`}
        />
        <button
          type="submit"
          className="px-3 py-1 rounded bg-[var(--color-accent-600)] text-white text-sm"
        >
          Replay selected
        </button>
      </form>
      <form id="replay-all" action={replayAllFiltered}>
        <ConfirmSubmitButton
          formId="replay-all"
          label={`Replay up to ${take} newest (filtered)`}
          confirmText={`Requeue up to ${take} notifications matching current filters?`}
          className="inline-block px-3 py-1 rounded bg-[var(--color-accent-600)] text-white text-sm"
        />
      </form>

      <div className="mt-3 flex gap-2 items-center">
        <span className="text-sm">
          Page {page} of {pages} — Total {total}
        </span>
        <nav className="ml-auto flex gap-2">
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
        </nav>
      </div>

      <div className="overflow-x-auto rounded-md border border-[var(--color-line)] bg-[var(--color-surface-raised)]">
        <table className={`${styles.table} w-full text-sm`}>
          <thead className="bg-[var(--color-surface)]">
            <tr className="text-left">
              <th className="px-3 py-2 font-medium" align="left">
                Id
              </th>
              <th className="px-3 py-2 font-medium" align="left">
                Kind
              </th>
              <th className="px-3 py-2 font-medium" align="left">
                To
              </th>
              <th className="px-3 py-2 font-medium" align="left">
                Status
              </th>
              <th className="px-3 py-2 font-medium" align="right">
                Attempts
              </th>
              <th className="px-3 py-2 font-medium" align="left">
                Created
              </th>
              <th className="px-3 py-2 font-medium" align="left">
                Error
              </th>
              <th className="px-3 py-2 font-medium" align="left">
                Actions
              </th>
            </tr>
          </thead>
          <tbody>
            {items.length === 0 ? (
              <tr>
                <td colSpan={8} className={`${styles.errorCell} px-3 py-4`}>
                  No items found for the selected filters.
                </td>
              </tr>
            ) : (
              items.map((n) => (
                <tr key={n.id} className={styles.row}>
                  <td className={`${styles.mono} px-3 py-2`}>{n.id}</td>
                  <td className="px-3 py-2">{n.kind}</td>
                  <td className="px-3 py-2">{n.toEmail}</td>
                  <td className="px-3 py-2">{n.status}</td>
                  <td className="px-3 py-2" align="right">
                    {n.attemptCount}
                  </td>
                  <td className="px-3 py-2">{new Date(n.createdAt).toLocaleString()}</td>
                  <td className={`${styles.errorCell} px-3 py-2`} title={n.lastError ?? ''}>
                    {n.lastError ?? ''}
                  </td>
                  <td className="px-3 py-2">
                    <form id={`replay-${n.id}`} action={replayOne}>
                      <input type="hidden" name="id" value={n.id} />
                      <ConfirmSubmitButton
                        formId={`replay-${n.id}`}
                        label="Replay"
                        confirmText="Requeue this notification?"
                        className="inline-block px-3 py-1 rounded bg-[var(--color-accent-600)] text-white text-sm"
                      />
                    </form>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
