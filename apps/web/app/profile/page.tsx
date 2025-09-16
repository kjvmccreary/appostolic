import React from 'react';
import { fetchFromProxy } from '../lib/serverFetch';
import { AvatarUpload } from '@/components/AvatarUpload';

export const dynamic = 'force-dynamic';

export default async function ProfilePage() {
  const res = await fetchFromProxy('/api-proxy/users/me');
  if (!res.ok) {
    return (
      <main id="main" className="container p-4">
        <h1 className="text-xl font-semibold">Profile</h1>
        <p className="text-red-500">Failed to load profile ({res.status}).</p>
      </main>
    );
  }
  const me = await res.json();
  const avatarUrl: string | undefined = me?.profile?.avatar?.url;
  const email: string = me?.email ?? '';

  return (
    <main id="main" className="container p-4 space-y-6">
      <h1 className="text-xl font-semibold">Your Profile</h1>
      <section className="flex items-center gap-4">
        <div className="h-16 w-16 overflow-hidden rounded-full border border-line bg-[var(--color-surface-raised)]">
          {avatarUrl ? (
            <img src={avatarUrl} alt="Avatar" className="h-full w-full object-cover" />
          ) : (
            <div className="flex h-full w-full items-center justify-center text-sm text-muted-foreground">
              No avatar
            </div>
          )}
        </div>
        <div>
          <div className="text-sm text-muted-foreground">Signed in as</div>
          <div>{email}</div>
        </div>
      </section>
      <section>
        <h2 className="mb-2 font-medium">Update avatar</h2>
        <AvatarUpload />
      </section>
    </main>
  );
}
