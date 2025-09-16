import { redirect } from 'next/navigation';
import { getServerSession } from 'next-auth';
import { authOptions } from '../../../src/lib/auth';
import { fetchFromProxy } from '../../lib/serverFetch';

export default async function AcceptInvitePage({
  searchParams,
}: {
  searchParams: { token?: string };
}) {
  const token = searchParams?.token;
  if (!token) {
    return (
      <div className="mx-auto max-w-lg p-6">
        <h1 className="text-xl font-semibold mb-2">Missing invite</h1>
        <p className="text-sm text-gray-600">No invite token was provided.</p>
      </div>
    );
  }

  const session = await getServerSession(authOptions);
  if (!session?.user?.email) {
    // Not signed in — send to login with next back to this page keeping token
    redirect(
      `/login?next=${encodeURIComponent(`/invite/accept?token=${encodeURIComponent(token)}`)}`,
    );
  }

  // Signed in: call internal proxy to forward auth headers and accept
  const res = await fetchFromProxy('/api-proxy/invites/accept', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ token }),
    cache: 'no-store',
  });

  if (!res.ok) {
    const text = await res.text();
    return (
      <div className="mx-auto max-w-lg p-6">
        <h1 className="text-xl font-semibold mb-2">Invite error</h1>
        <pre className="text-sm whitespace-pre-wrap break-words">{text}</pre>
      </div>
    );
  }

  await res.json();

  // After accepting, redirect to selection if user has multiple memberships.
  // We don't have client-side membership list here; let the app landing handle.
  redirect('/studio');
}
