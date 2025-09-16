import React from 'react';
import { fetchFromProxy } from '../lib/serverFetch';
import { ProfileView } from './ProfileView';
import { ProfileEditForm } from './ProfileEditForm';
import { ProfileGuardrailsForm } from './ProfileGuardrailsForm';

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
    guardrails?: {
      denominationAlignment?: string;
      favoriteAuthors?: string[];
      favoriteBooks?: string[];
      notes?: string;
    } | null;
    preferences?: { lessonFormat?: string } | null;
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

  const guardrailsInitial = {
    denominationAlignment: me.profile?.guardrails?.denominationAlignment,
    favoriteAuthors: me.profile?.guardrails?.favoriteAuthors || [],
    favoriteBooks: me.profile?.guardrails?.favoriteBooks || [],
    notes: me.profile?.guardrails?.notes,
    lessonFormat: me.profile?.preferences?.lessonFormat,
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
      <section className="space-y-4" aria-labelledby="profile-guardrails-heading">
        <h2 id="profile-guardrails-heading" className="text-lg font-medium">
          Guardrails & Preferences
        </h2>
        <ProfileGuardrailsForm initial={guardrailsInitial} />
      </section>
    </main>
  );
}
