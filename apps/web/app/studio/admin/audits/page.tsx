import { getServerSession } from 'next-auth';
import { authOptions } from '../../../../src/lib/auth';
import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { fetchFromProxy } from '../../../../app/lib/serverFetch';
import { roleNamesFromFlags } from '../../../../src/lib/roles';

type LegacyRole = 'Owner' | 'Admin' | 'Editor' | 'Viewer';

type AuditRow = {
  id: string;
  userId: string;
  changedByUserId: string;
  changedByEmail: string;
  oldRoles: number; // flags value
  newRoles: number; // flags value
  changedAt: string;
};

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

function buildQuery(params: Record<string, string | undefined>): string {
  const sp = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v) sp.set(k, v);
  return sp.toString();
}

export default async function AuditsPage({
  searchParams,
}: {
  searchParams?: Record<string, string | string[]>;
}) {
  const session = await getServerSession(authOptions);
  if (!session?.user?.email) redirect('/login');

  const memberships =
    (
      session as unknown as {
        memberships?: { tenantId: string; tenantSlug: string; role: LegacyRole }[];
      } | null
    )?.memberships ?? [];
  const currentTenant =
    (session as unknown as { tenant?: string } | null)?.tenant ||
    cookies().get('selected_tenant')?.value;
  const mine = memberships.find((m) => m.tenantSlug === currentTenant);
  if (!mine) redirect('/select-tenant');

  // Enforce admin in UI: render 403 message rather than redirecting.
  if (mine.role !== 'Owner' && mine.role !== 'Admin') {
    return <div>403 — Access denied</div>;
  }

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
  if (!res.ok) return <div>Failed to load audits</div>;
  const total = Number(res.headers.get('X-Total-Count') ?? '0');
  const raw = (await res.json()) as AuditRow[];
  const rows = raw.map((r) => ({
    ...r,
    oldNames: roleNamesFromFlags(r.oldRoles).join(', ') || 'None',
    newNames: roleNamesFromFlags(r.newRoles).join(', ') || 'None',
  }));

  const page = Math.floor(Number(skip) / Number(take)) + 1;
  const pages = Math.max(1, Math.ceil(total / Number(take)));

  // Minimal UI: table + basic pager + filter form
  return (
    <div>
      <h1>Audits — {mine.tenantSlug}</h1>
      <form action="/studio/admin/audits" method="get" className="grid gap-2 grid-cols-6 mb-3">
        <input type="hidden" name="take" value={take} />
        <input type="hidden" name="skip" value={skip} />
        <input name="userId" placeholder="User ID" defaultValue={userId ?? ''} />
        <input name="changedByUserId" placeholder="Actor ID" defaultValue={changedByUserId ?? ''} />
        <input name="from" placeholder="From (ISO)" defaultValue={from ?? ''} />
        <input name="to" placeholder="To (ISO)" defaultValue={to ?? ''} />
        <button type="submit">Apply</button>
        <a href="/studio/admin/audits">Reset</a>
      </form>

      <table>
        <thead>
          <tr>
            <th>When</th>
            <th>User</th>
            <th>Actor</th>
            <th>Old Flags</th>
            <th>New Flags</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => {
            return (
              <tr key={r.id}>
                <td>{new Date(r.changedAt).toLocaleString()}</td>
                <td>{r.userId}</td>
                <td title={r.changedByEmail}>{r.changedByUserId}</td>
                <td title={String(r.oldRoles)}>{r.oldNames}</td>
                <td title={String(r.newRoles)}>{r.newNames}</td>
              </tr>
            );
          })}
        </tbody>
      </table>

      <div className="mt-3 flex gap-2 items-center">
        <span>
          Page {page} of {pages} — Total {total}
        </span>
        <nav className="flex gap-2">
          {page > 1 && (
            <a
              href={`/studio/admin/audits?${buildQuery({ take, skip: String((page - 2) * Number(take)), userId, changedByUserId, from, to })}`}
            >
              Prev
            </a>
          )}
          {page < pages && (
            <a
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
