import React from 'react';
import { fetchFromProxy } from '../lib/serverFetch';
import { ProfileView } from './ProfileView';
import { ProfileEditForm } from './ProfileEditForm';

export const dynamic = 'force-dynamic';

// Shape of the user profile data we depend on for read-only display
interface UserProfileDto {
  email: string;
  profile?: {
    avatar?: { url?: string | null } | null;
    name?: { first?: string; last?: string; display?: string } | null;
    contact?: { phone?: string; timezone?: string; locale?: string } | null;
    social?: {
      website?: string;
      twitter?: string;
      facebook?: string;
      instagram?: string;
      youtube?: string;
      linkedin?: string;
    } | null;
  } | null;
}

async function loadProfile(): Promise<UserProfileDto | null> {
  const res = await fetchFromProxy('/api-proxy/users/me');
  if (!res.ok) return null;
  try {
    return (await res.json()) as UserProfileDto;
  } catch {
    return null;
  }
}

export default async function ProfilePage() {
  const me = await loadProfile();

  if (!me) {
    return (
      <main id="main" className="container p-4 space-y-4" aria-labelledby="profile-heading">
        <h1 id="profile-heading" className="text-xl font-semibold">
          Your Profile
        </h1>
        <p className="text-sm text-red-500">Unable to load your profile right now.</p>
      </main>
    );
  }

  const initial = {
    display: me.profile?.name?.display,
    first: me.profile?.name?.first,
    last: me.profile?.name?.last,
    phone: me.profile?.contact?.phone,
    timezone: me.profile?.contact?.timezone,
    locale: me.profile?.contact?.locale,
    website: me.profile?.social?.website,
    twitter: me.profile?.social?.twitter,
    facebook: me.profile?.social?.facebook,
    instagram: me.profile?.social?.instagram,
    youtube: me.profile?.social?.youtube,
    linkedin: me.profile?.social?.linkedin,
  };

  return (
    <main id="main" className="container p-4 space-y-10" aria-labelledby="profile-heading">
      <header className="space-y-4">
        <h1 id="profile-heading" className="text-xl font-semibold">
          Your Profile
        </h1>
        <ProfileView email={me.email} avatarUrl={me.profile?.avatar?.url || undefined} />
      </header>
      <section className="space-y-4" aria-labelledby="profile-edit-heading">
        <h2 id="profile-edit-heading" className="text-lg font-medium">
          Personal Information
        </h2>
        <ProfileEditForm initial={initial} />
      </section>
    </main>
  );
}
