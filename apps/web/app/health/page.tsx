import { cookies } from 'next/headers';
import { getServerSession } from 'next-auth';
import { authOptions } from '../../src/lib/auth';

export const dynamic = 'force-dynamic';

export default async function PublicHealthPage() {
  const session = await getServerSession(authOptions);
  const email = session?.user?.email ?? null;
  const cookieTenant = cookies().get('selected_tenant')?.value ?? null;

  return (
    <main className="mx-auto max-w-xl p-6">
      <h1 className="text-xl font-semibold mb-4">Health (Public)</h1>
      <div className="space-y-2">
        <div>
          <span className="font-medium">Session email:</span>{' '}
          <code>{email ?? '— (not signed in)'}</code>
        </div>
        <div>
          <span className="font-medium">selected_tenant cookie:</span>{' '}
          <code>{cookieTenant ?? '—'}</code>
        </div>
        <p className="text-sm text-gray-600">
          Tip: /dev/health is protected and requires login; use this page before signing in.
        </p>
      </div>
    </main>
  );
}
