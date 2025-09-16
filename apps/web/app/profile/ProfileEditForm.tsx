'use client';
import React, { useState } from 'react';
import type { EditableProfileFields } from './ProfileView';

interface ProfileEditFormProps {
  initial: EditableProfileFields;
  onSaved?: (updated: EditableProfileFields) => void;
}

// Lightweight URL validator (allow blank, require protocol or auto-prepend https on submit)
function normalizeUrl(input: string | undefined): string | undefined {
  if (!input) return undefined;
  const trimmed = input.trim();
  if (!trimmed) return undefined;
  if (/^https?:\/\//i.test(trimmed)) return trimmed;
  // If it looks like a domain, prepend https
  if (/^[\w.-]+\.[a-z]{2,}$/i.test(trimmed)) return `https://${trimmed}`;
  return trimmed; // leave as-is; backend will drop if invalid
}

interface ProfilePatchName {
  first?: string;
  last?: string;
  display?: string;
}
interface ProfilePatchContact {
  phone?: string;
  timezone?: string;
  locale?: string;
}
interface ProfilePatchSocial {
  website?: string;
  twitter?: string;
  facebook?: string;
  instagram?: string;
  youtube?: string;
  linkedin?: string;
}
interface ProfilePatch {
  name?: ProfilePatchName;
  contact?: ProfilePatchContact;
  social?: ProfilePatchSocial;
}

function toPatch(fields: EditableProfileFields) {
  // Construct JSON merge patch structure matching backend schema
  const name: ProfilePatchName = {};
  if (fields.first) name.first = fields.first.trim();
  if (fields.last) name.last = fields.last.trim();
  if (fields.display) name.display = fields.display.trim();
  const contact: ProfilePatchContact = {};
  if (fields.phone) contact.phone = fields.phone.trim();
  if (fields.timezone) contact.timezone = fields.timezone.trim();
  if (fields.locale) contact.locale = fields.locale.trim();
  const social: ProfilePatchSocial = {};
  (['website', 'twitter', 'facebook', 'instagram', 'youtube', 'linkedin'] as const).forEach(
    (key) => {
      const v = fields[key];
      if (v) social[key] = normalizeUrl(v);
    },
  );
  const profile: ProfilePatch = {};
  if (Object.keys(name).length) profile.name = name;
  if (Object.keys(contact).length) profile.contact = contact;
  if (Object.keys(social).length) profile.social = social;
  return { profile };
}

export const ProfileEditForm: React.FC<ProfileEditFormProps> = ({ initial, onSaved }) => {
  const [fields, setFields] = useState<EditableProfileFields>(initial);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  function update<K extends keyof EditableProfileFields>(key: K, value: string) {
    setFields((f) => ({ ...f, [key]: value }));
  }

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError(null);
    setSuccess(null);
    try {
      const patch = toPatch(fields);
      const res = await fetch('/api-proxy/users/me', {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(patch),
      });
      if (!res.ok) {
        setError(`Update failed (${res.status}).`);
      } else {
        setSuccess('Profile updated.');
        onSaved?.(fields);
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Unexpected error';
      setError(message);
    } finally {
      setSaving(false);
    }
  }

  const inputCls = 'w-full rounded border border-line bg-transparent px-2 py-1 text-sm';
  const fieldsetCls = 'grid gap-4 sm:grid-cols-2';

  return (
    <form
      onSubmit={onSubmit}
      className="space-y-6"
      aria-describedby={error ? 'profile-error' : undefined}
    >
      <fieldset className={fieldsetCls} disabled={saving}>
        <div>
          <label htmlFor="pf-display" className="block text-xs font-medium mb-1">
            Display Name
          </label>
          <input
            id="pf-display"
            className={inputCls}
            value={fields.display || ''}
            onChange={(e) => update('display', e.target.value)}
          />
        </div>
        <div>
          <label htmlFor="pf-first" className="block text-xs font-medium mb-1">
            First Name
          </label>
          <input
            id="pf-first"
            className={inputCls}
            value={fields.first || ''}
            onChange={(e) => update('first', e.target.value)}
          />
        </div>
        <div>
          <label htmlFor="pf-last" className="block text-xs font-medium mb-1">
            Last Name
          </label>
          <input
            id="pf-last"
            className={inputCls}
            value={fields.last || ''}
            onChange={(e) => update('last', e.target.value)}
          />
        </div>
        <div>
          <label htmlFor="pf-phone" className="block text-xs font-medium mb-1">
            Phone
          </label>
          <input
            id="pf-phone"
            className={inputCls}
            value={fields.phone || ''}
            onChange={(e) => update('phone', e.target.value)}
          />
        </div>
        <div>
          <label htmlFor="pf-timezone" className="block text-xs font-medium mb-1">
            Timezone
          </label>
          <input
            id="pf-timezone"
            className={inputCls}
            value={fields.timezone || ''}
            onChange={(e) => update('timezone', e.target.value)}
            placeholder="e.g. America/Chicago"
          />
        </div>
        <div>
          <label htmlFor="pf-locale" className="block text-xs font-medium mb-1">
            Locale
          </label>
          <input
            id="pf-locale"
            className={inputCls}
            value={fields.locale || ''}
            onChange={(e) => update('locale', e.target.value)}
            placeholder="e.g. en-US"
          />
        </div>
      </fieldset>

      <div>
        <h3 className="text-sm font-semibold mb-2">Social Links</h3>
        <fieldset className={fieldsetCls} disabled={saving}>
          {(['website', 'twitter', 'facebook', 'instagram', 'youtube', 'linkedin'] as const).map(
            (k) => (
              <div key={k}>
                <label htmlFor={`pf-${k}`} className="block text-xs font-medium mb-1 capitalize">
                  {k}
                </label>
                <input
                  id={`pf-${k}`}
                  className={inputCls}
                  value={fields[k] || ''}
                  onChange={(e) => update(k, e.target.value)}
                />
              </div>
            ),
          )}
        </fieldset>
      </div>

      {error && (
        <div
          id="profile-error"
          role="alert"
          className="rounded border border-red-400 bg-red-50 p-2 text-sm text-red-700"
        >
          {error}
        </div>
      )}
      {success && (
        <div
          role="status"
          className="rounded border border-green-400 bg-green-50 p-2 text-sm text-green-700"
        >
          {success}
        </div>
      )}
      <div>
        <button
          type="submit"
          disabled={saving}
          className="inline-flex items-center rounded bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground disabled:opacity-50"
        >
          {saving ? 'Saving…' : 'Save Changes'}
        </button>
      </div>
    </form>
  );
};
