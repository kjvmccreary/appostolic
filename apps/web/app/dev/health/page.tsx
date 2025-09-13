import { cookies } from 'next/headers';
import { getServerSession } from 'next-auth';
import { authOptions } from '../../../src/lib/auth';

export const dynamic = 'force-dynamic';

export default async function DevHealthPage() {
  const session = await getServerSession(authOptions);
  const cookieTenant = cookies().get('selected_tenant')?.value ?? null;
  const email = session?.user?.email ?? null;
  const memberships =
    (session as unknown as { memberships?: Array<{ tenantSlug: string }> })?.memberships ?? [];
  const selected = (session as unknown as { tenant?: string })?.tenant ?? cookieTenant;

  return (
    <main className="mx-auto max-w-xl p-6">
      <h1 className="text-xl font-semibold mb-4">Dev Health</h1>
      <div className="space-y-2">
        <div>
          <span className="font-medium">Session email:</span> <code>{email ?? '—'}</code>
        </div>
        <div>
          <span className="font-medium">Selected tenant:</span> <code>{selected ?? '—'}</code>
        </div>
        <div>
          <span className="font-medium">Memberships:</span>{' '}
          <code>{memberships.map((m) => m.tenantSlug).join(', ') || '—'}</code>
        </div>
        <p className="text-sm text-gray-600">
          Tip: If selected tenant is empty, protected pages will redirect you to /select-tenant.
        </p>
      </div>
    </main>
  );
}
