import React from 'react';
import { fetchFromProxy } from '../lib/serverFetch';
import { ProfileView } from './ProfileView';
import { ProfileEditForm } from './ProfileEditForm';
import { ProfileGuardrailsForm } from './ProfileGuardrailsForm';
import { BioEditor } from './BioEditor';
import { AvatarUpload } from '../../src/components/AvatarUpload';

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
    bio?: { format?: string; content?: string } | null;
    presets?: {
      denominations?: string[];
    } | null;
  } | null;
}

// Runtime shape of the stored profile JSON object (after flattening)
interface ProfileJson {
  name?: { first?: string; last?: string; display?: string };
  contact?: { phone?: string; timezone?: string; locale?: string };
  social?: {
    website?: string;
    twitter?: string;
    facebook?: string;
    instagram?: string;
    youtube?: string;
    linkedin?: string;
  };
  guardrails?: {
    denominationAlignment?: string;
    favoriteAuthors?: string[];
    favoriteBooks?: string[];
    notes?: string;
  };
  preferences?: { lessonFormat?: string };
  bio?: { format?: string; content?: string };
  presets?: { denominations?: string[] };
  profile?: unknown; // legacy nested object (ignored after flatten)
}

interface DenominationPreset {
  id: string;
  name: string;
  notes?: string;
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

async function loadDenominationPresets(): Promise<DenominationPreset[] | null> {
  const res = await fetchFromProxy('/api-proxy/metadata/denominations');
  if (!res.ok) return null;
  try {
    const json = (await res.json()) as { presets?: DenominationPreset[] };
    if (Array.isArray(json.presets)) return json.presets;
    return null;
  } catch {
    return null;
  }
}

export default async function ProfilePage() {
  const [me, denominationPresets] = await Promise.all([loadProfile(), loadDenominationPresets()]);

  if (!me) {
    return (
      <main id="main" className="mx-auto max-w-3xl p-6 space-y-6" aria-labelledby="profile-heading">
        <header className="space-y-2">
          <h1 id="profile-heading" className="text-2xl font-semibold">
            Your Profile
          </h1>
          <p className="text-sm text-muted">
            We couldn't load your data. Refresh or try again shortly.
          </p>
        </header>
        <p className="text-sm text-red-500">Unable to load your profile right now.</p>
      </main>
    );
  }

  // Temporary backward compatibility: earlier client incorrectly nested data under profile.profile
  const rawProfile: unknown = me.profile;
  function hasNestedProfile(o: unknown): o is { profile: ProfileJson } {
    return !!o && typeof o === 'object' && 'profile' in o;
  }
  // Flatten legacy shape: earlier versions incorrectly nested data under profile.profile.
  // We want root-level (newly saved) fields to win over legacy nested ones if both exist.
  const flattened: ProfileJson | null = hasNestedProfile(rawProfile)
    ? { ...rawProfile.profile, ...(rawProfile as ProfileJson) }
    : (rawProfile as ProfileJson) || null;

  const initial = {
    display: flattened?.name?.display,
    first: flattened?.name?.first,
    last: flattened?.name?.last,
    phone: flattened?.contact?.phone,
    timezone: flattened?.contact?.timezone,
    website: flattened?.social?.website,
    twitter: flattened?.social?.twitter,
    facebook: flattened?.social?.facebook,
    instagram: flattened?.social?.instagram,
    youtube: flattened?.social?.youtube,
    linkedin: flattened?.social?.linkedin,
  };

  const guardrailsInitial = {
    denominationAlignment: flattened?.guardrails?.denominationAlignment,
    favoriteAuthors: flattened?.guardrails?.favoriteAuthors || [],
    favoriteBooks: flattened?.guardrails?.favoriteBooks || [],
    notes: flattened?.guardrails?.notes,
    lessonFormat: flattened?.preferences?.lessonFormat,
    denominations: flattened?.presets?.denominations || [],
  };

  const bioInitial = flattened?.bio
    ? { format: flattened.bio.format, content: flattened.bio.content }
    : undefined;

  return (
    <main id="main" className="mx-auto max-w-3xl p-6 space-y-10" aria-labelledby="profile-heading">
      {/* Page header: mirrors dashboard typography scale (2xl heading + helper text) */}
      <header className="space-y-3">
        <h1 id="profile-heading" className="text-2xl font-semibold">
          Your Profile
        </h1>
        <p className="text-sm text-muted max-w-prose">
          Manage your personal details, doctrinal guardrails, learning preferences, and bio. Each
          section saves independently.
        </p>
        {/* Avatar section with inline upload */}
        <ProfileView email={me.email} avatarUrl={me.profile?.avatar?.url || undefined} />
        <div>
          {/* AvatarUpload is a client component; avoid passing function props from this server file. */}
          <AvatarUpload />
        </div>
      </header>

      {/* Personal Information Section Card */}
      <section
        className="rounded-lg border border-line bg-[var(--color-canvas)] p-6 shadow-sm space-y-4"
        aria-labelledby="profile-edit-heading"
      >
        {/* Section heading follows consistent scale (lg) */}
        <h2 id="profile-edit-heading" className="text-lg font-medium">
          Personal Information
        </h2>
        {/* Form handles its own internal validation + submission states */}
        <ProfileEditForm initial={initial} />
      </section>

      {/* Guardrails & Preferences Section Card */}
      <section
        className="rounded-lg border border-line bg-[var(--color-canvas)] p-6 shadow-sm space-y-4"
        aria-labelledby="profile-guardrails-heading"
      >
        <h2 id="profile-guardrails-heading" className="text-lg font-medium">
          Guardrails & Preferences
        </h2>
        <ProfileGuardrailsForm
          initial={guardrailsInitial}
          presets={denominationPresets || undefined}
        />
      </section>

      {/* Bio Section Card */}
      <section
        className="rounded-lg border border-line bg-[var(--color-canvas)] p-6 shadow-sm space-y-4"
        aria-labelledby="profile-bio-heading"
      >
        <h2 id="profile-bio-heading" className="text-lg font-medium">
          Bio
        </h2>
        <BioEditor initial={bioInitial} />
      </section>
    </main>
  );
}
