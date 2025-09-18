import { getServerSession } from 'next-auth';
import { authOptions } from '../../../../src/lib/auth';
import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { fetchFromProxy } from '../../../../app/lib/serverFetch';
import { mapAuditRows, type AuditRow } from './mapAuditRows';
import { computeBooleansForTenant } from '../../../../src/lib/roles';

type LegacyRole = 'Owner' | 'Admin' | 'Editor' | 'Viewer';

// page-only exports must be limited; mapping moved to mapAuditRows.ts

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

function buildQuery(params: Record<string, string | undefined>): string {
  const sp = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v) sp.set(k, v);
  return sp.toString();
}

export default async function AuditsPage(props: {
  searchParams?: Record<string, string | string[]>;
}) {
  const { searchParams } = props || {};
  const session = await getServerSession(authOptions);
  if (!session?.user?.email) redirect('/login');

  const memberships =
    (
      session as unknown as {
        memberships?: {
          tenantId: string;
          tenantSlug: string;
          role?: LegacyRole;
          roles?: string[];
        }[];
      } | null
    )?.memberships ?? [];
  const currentTenant =
    (session as unknown as { tenant?: string } | null)?.tenant ||
    cookies().get('selected_tenant')?.value;
  const mine = memberships.find((m) => m.tenantSlug === currentTenant);
  if (!mine) redirect('/select-tenant');

  // Flags-only admin gating via computeBooleansForTenant (legacy role ignored)
  const { isAdmin } = computeBooleansForTenant(
    memberships.map((m) => ({
      tenantId: m.tenantId,
      tenantSlug: m.tenantSlug,
      role: 'Viewer', // placeholder legacy role
      roles: m.roles || [],
    })) as unknown as Parameters<typeof computeBooleansForTenant>[0],
    mine.tenantSlug,
  );
  if (!isAdmin) return <div>403 — Access denied</div>;

  const take = String(
    (Array.isArray(searchParams?.take) ? searchParams?.take[0] : searchParams?.take) ?? '50',
  );
  const skip = String(
    (Array.isArray(searchParams?.skip) ? searchParams?.skip[0] : searchParams?.skip) ?? '0',
  );
  const userId = Array.isArray(searchParams?.userId)
    ? searchParams?.userId[0]
    : (searchParams?.userId as string | undefined);
  const changedByUserId = Array.isArray(searchParams?.changedByUserId)
    ? searchParams?.changedByUserId[0]
    : (searchParams?.changedByUserId as string | undefined);
  const from = Array.isArray(searchParams?.from)
    ? searchParams?.from[0]
    : (searchParams?.from as string | undefined);
  const to = Array.isArray(searchParams?.to)
    ? searchParams?.to[0]
    : (searchParams?.to as string | undefined);

  const qs = buildQuery({ take, skip, userId, changedByUserId, from, to });
  const url = `/api-proxy/tenants/${mine.tenantId}/audits${qs ? `?${qs}` : ''}`;
  const res = await fetchFromProxy(url, { cache: 'no-store' });
  if (!res.ok)
    return (
      <div className="mx-auto max-w-5xl p-4">
        <h1 className="text-xl font-semibold mb-2">Audits</h1>
        <div className="rounded-md border border-[var(--color-line)] bg-red-50 p-3 text-sm text-red-900">
          Failed to load audits
        </div>
      </div>
    );
  const total = Number(res.headers.get('X-Total-Count') ?? '0');
  const raw = (await res.json()) as AuditRow[];
  const rows = mapAuditRows(raw);

  const page = Math.floor(Number(skip) / Number(take)) + 1;
  const pages = Math.max(1, Math.ceil(total / Number(take)));

  // Polished UI: layout container, filters with presets, styled table and pager
  return (
    <div className="mx-auto max-w-5xl p-4">
      <h1 className="text-xl font-semibold mb-2">Audits — {mine.tenantSlug}</h1>

      <form
        action="/studio/admin/audits"
        method="get"
        className="mb-3 grid grid-cols-12 gap-2 items-end"
      >
        <input type="hidden" name="take" value={take} />
        <input type="hidden" name="skip" value={skip} />
        <div className="col-span-3">
          <label className="block text-xs mb-1">User ID</label>
          <input
            name="userId"
            placeholder="User ID"
            defaultValue={userId ?? ''}
            className="w-full rounded border px-2 py-1"
          />
        </div>
        <div className="col-span-3">
          <label className="block text-xs mb-1">Actor ID</label>
          <input
            name="changedByUserId"
            placeholder="Actor ID"
            defaultValue={changedByUserId ?? ''}
            className="w-full rounded border px-2 py-1"
          />
        </div>
        <div className="col-span-3">
          <label className="block text-xs mb-1">From (ISO)</label>
          <input
            name="from"
            placeholder="YYYY-MM-DD"
            defaultValue={from ?? ''}
            className="w-full rounded border px-2 py-1"
          />
        </div>
        <div className="col-span-3">
          <label className="block text-xs mb-1">To (ISO)</label>
          <input
            name="to"
            placeholder="YYYY-MM-DD"
            defaultValue={to ?? ''}
            className="w-full rounded border px-2 py-1"
          />
        </div>
        <div className="col-span-12 flex items-center gap-2">
          <button
            type="submit"
            className="px-3 py-1 rounded bg-[var(--color-accent-600)] text-white text-sm"
          >
            Apply
          </button>
          <a className="text-sm underline" href="/studio/admin/audits">
            Reset
          </a>
          <span className="ml-auto text-xs text-muted">Quick ranges:</span>
          <a
            className="text-xs underline"
            href={`/studio/admin/audits?${buildQuery({ take, skip: '0', from: new Date(Date.now() - 7 * 864e5).toISOString().slice(0, 10), to: new Date().toISOString().slice(0, 10), userId, changedByUserId })}`}
          >
            Last 7d
          </a>
          <a
            className="text-xs underline"
            href={`/studio/admin/audits?${buildQuery({ take, skip: '0', from: new Date(Date.now() - 30 * 864e5).toISOString().slice(0, 10), to: new Date().toISOString().slice(0, 10), userId, changedByUserId })}`}
          >
            Last 30d
          </a>
        </div>
      </form>

      <div className="overflow-x-auto rounded-md border border-[var(--color-line)] bg-[var(--color-surface-raised)]">
        {rows.length === 0 ? (
          <div className="p-6 text-sm text-muted">No audits found for the selected range.</div>
        ) : (
          <table className="w-full text-sm">
            <thead className="bg-[var(--color-surface)]">
              <tr className="text-left">
                <th className="px-3 py-2 font-medium">When</th>
                <th className="px-3 py-2 font-medium">User</th>
                <th className="px-3 py-2 font-medium">Actor</th>
                <th className="px-3 py-2 font-medium">Old Flags</th>
                <th className="px-3 py-2 font-medium">New Flags</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.id} className="border-t border-[var(--color-line)]">
                  <td className="px-3 py-2 whitespace-nowrap">
                    {new Date(r.changedAt).toLocaleString()}
                  </td>
                  <td className="px-3 py-2 font-mono text-xs">{r.userId}</td>
                  <td className="px-3 py-2 font-mono text-xs" title={r.changedByEmail}>
                    {r.changedByUserId}
                  </td>
                  <td className="px-3 py-2" title={String(r.oldRoles)}>
                    {r.oldNames}
                  </td>
                  <td className="px-3 py-2" title={String(r.newRoles)}>
                    {r.newNames}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <div className="mt-3 flex gap-2 items-center">
        <span className="text-sm">
          Page {page} of {pages} — Total {total}
        </span>
        <nav className="ml-auto flex gap-2">
          {page > 1 && (
            <a
              className="px-3 py-1 rounded border text-sm"
              href={`/studio/admin/audits?${buildQuery({ take, skip: String((page - 2) * Number(take)), userId, changedByUserId, from, to })}`}
            >
              Prev
            </a>
          )}
          {page < pages && (
            <a
              className="px-3 py-1 rounded border text-sm"
              href={`/studio/admin/audits?${buildQuery({ take, skip: String(page * Number(take)), userId, changedByUserId, from, to })}`}
            >
              Next
            </a>
          )}
        </nav>
      </div>
    </div>
  );
}
