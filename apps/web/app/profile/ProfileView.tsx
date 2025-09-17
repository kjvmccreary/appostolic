'use client';
import React from 'react';

export interface ProfileViewProps {
  email: string;
  avatarUrl?: string | null;
}

export interface EditableProfileFields {
  display?: string;
  first?: string;
  last?: string;
  phone?: string;
  timezone?: string;
  website?: string;
  twitter?: string;
  facebook?: string;
  instagram?: string;
  youtube?: string;
  linkedin?: string;
}

// Presentational component for the read-only profile view (UPROF-05)
// Keeps server page minimalist and provides a stable test target.
export const ProfileView: React.FC<ProfileViewProps> = ({ email, avatarUrl }) => {
  return (
    <section className="flex items-center gap-4" aria-label="Identity summary">
      <div className="h-16 w-16 overflow-hidden rounded-full border border-line bg-[var(--color-surface-raised)]">
        {avatarUrl ? (
          <img
            src={avatarUrl}
            alt=""
            data-testid="avatar-img"
            className="h-full w-full object-cover"
          />
        ) : (
          <div
            className="flex h-full w-full items-center justify-center text-sm text-muted-foreground"
            data-testid="no-avatar"
          >
            No avatar
          </div>
        )}
      </div>
      <div className="min-w-0">
        <div className="text-sm text-muted-foreground">Signed in as</div>
        <div className="truncate" data-testid="profile-email">
          {email}
        </div>
      </div>
    </section>
  );
};
