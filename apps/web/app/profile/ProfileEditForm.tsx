'use client';
import React, { useEffect, useState } from 'react';
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

// Patch interfaces allow null to explicitly clear previous non-empty values on the backend merge.
interface ProfilePatchName {
  first?: string | null;
  last?: string | null;
  display?: string | null;
}
interface ProfilePatchContact {
  phone?: string | null;
  timezone?: string | null;
}
interface ProfilePatchSocial {
  website?: string | null;
  twitter?: string | null;
  facebook?: string | null;
  instagram?: string | null;
  youtube?: string | null;
  linkedin?: string | null;
}
interface ProfilePatch {
  name?: ProfilePatchName;
  contact?: ProfilePatchContact;
  social?: ProfilePatchSocial;
}

/**
 * Build a minimal merge patch describing changes between a baseline profile snapshot and the
 * current in‑form values. Non-empty changes are included with trimmed strings; fields that were
 * previously non-empty but are now empty are sent explicitly as null so the backend DeepMerge
 * logic will clear them. Unchanged fields are omitted to avoid unnecessary writes.
 */
function buildPatch(baseline: EditableProfileFields, current: EditableProfileFields): ProfilePatch {
  const patch: ProfilePatch = {};

  // Name fields (clear via null)
  const name: ProfilePatchName = {};
  (['first', 'last', 'display'] as const).forEach((k) => {
    const before = (baseline[k] || '').trim();
    const after = (current[k] || '').trim();
    if (after) {
      if (after !== before) name[k] = after; // changed value
    } else if (before) {
      // previously had a value, now blank -> explicit null to clear
      name[k] = null;
    }
  });
  if (Object.keys(name).length) patch.name = name;

  // Contact fields (currently only timezone & phone). Apply same clear semantics for consistency.
  const contact: ProfilePatchContact = {};
  (['phone', 'timezone'] as const).forEach((k) => {
    const before = (baseline[k] || '').trim();
    const after = (current[k] || '').trim();
    if (after) {
      if (after !== before) contact[k] = after;
    } else if (before) {
      contact[k] = null;
    }
  });
  if (Object.keys(contact).length) patch.contact = contact;

  // Social links: normalize non-empty; allow explicit null to clear removed entries.
  const social: ProfilePatchSocial = {};
  (['website', 'twitter', 'facebook', 'instagram', 'youtube', 'linkedin'] as const).forEach(
    (k) => {
      const beforeRaw = baseline[k];
      const afterRaw = current[k];
      const before = beforeRaw ? beforeRaw.trim() : '';
      const normalizedAfter = afterRaw ? normalizeUrl(afterRaw) : undefined;
      const after = normalizedAfter ? normalizedAfter.trim() : '';
      if (after) {
        if (after !== before) social[k] = after;
      } else if (before) {
        social[k] = null; // cleared
      }
    },
  );
  if (Object.keys(social).length) patch.social = social;

  return patch;
}

export const ProfileEditForm: React.FC<ProfileEditFormProps> = ({ initial, onSaved }) => {
  const [fields, setFields] = useState<EditableProfileFields>(initial);
  // Baseline tracks last-saved (or initial) values to compute a minimal diff & send nulls for clears.
  const [baseline, setBaseline] = useState<EditableProfileFields>(initial);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [timezones, setTimezones] = useState<string[]>([]);

  // Populate timezone list (Intl.supportedValuesOf if available, else fallback curated list)
  useEffect(() => {
    function loadZones(): string[] {
      try {
        // Access dynamically to avoid type issues on older TS lib versions
        const maybeIntl = Intl as unknown as { supportedValuesOf?: (kind: string) => string[] };
        if (typeof maybeIntl.supportedValuesOf === 'function') {
          const vals: string[] = maybeIntl.supportedValuesOf('timeZone');
          if (Array.isArray(vals) && vals.length > 0) return vals;
        }
      } catch {
        /* ignore */
      }
      // Fallback subset (common representations) — keep alphabetized
      return [
        'Africa/Johannesburg',
        'America/Chicago',
        'America/Denver',
        'America/Los_Angeles',
        'America/New_York',
        'America/Phoenix',
        'America/Sao_Paulo',
        'Asia/Dubai',
        'Asia/Hong_Kong',
        'Asia/Kolkata',
        'Asia/Seoul',
        'Asia/Shanghai',
        'Asia/Singapore',
        'Asia/Tokyo',
        'Australia/Brisbane',
        'Australia/Melbourne',
        'Australia/Sydney',
        'Europe/Amsterdam',
        'Europe/Berlin',
        'Europe/London',
        'Europe/Madrid',
        'Europe/Paris',
        'Europe/Rome',
        'Pacific/Auckland',
        'UTC',
      ];
    }
    setTimezones(loadZones());
  }, []);

  function update<K extends keyof EditableProfileFields>(key: K, value: string) {
    setFields((f) => ({ ...f, [key]: value }));
  }

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError(null);
    setSuccess(null);
    try {
      const patch = buildPatch(baseline, fields);
      const res = await fetch('/api-proxy/users/me', {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(patch),
      });
      if (!res.ok) {
        setError(`Update failed (${res.status}).`);
      } else {
        setSuccess('Profile updated.');
        // Update baseline so subsequent edits diff against newly-saved values.
        setBaseline(fields);
        onSaved?.(fields);
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Unexpected error';
      setError(message);
    } finally {
      setSaving(false);
    }
  }

  // Canonical input styling (aligned with Login form for visual consistency)
  const inputCls =
    'w-full rounded-md border border-line bg-[var(--color-surface)] px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-[var(--color-accent-600)]';
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
          <select
            id="pf-timezone"
            className={inputCls}
            value={fields.timezone || ''}
            onChange={(e) => update('timezone', e.target.value)}
          >
            <option value="">Select timezone…</option>
            {timezones.map((tz) => (
              <option key={tz} value={tz}>
                {tz}
              </option>
            ))}
          </select>
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
          className="inline-flex h-9 items-center rounded-md bg-[var(--color-accent-600)] px-3 text-sm font-medium text-white disabled:opacity-60"
        >
          {saving ? 'Saving…' : 'Save Changes'}
        </button>
      </div>
    </form>
  );
};
