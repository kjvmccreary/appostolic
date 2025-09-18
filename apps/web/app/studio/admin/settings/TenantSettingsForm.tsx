'use client';
import React, { useState } from 'react';

type SocialKeys = 'twitter' | 'facebook' | 'instagram' | 'youtube' | 'linkedin';

type TenantSettings = {
  displayName?: string;
  contact?: { email?: string; website?: string };
  social?: Partial<Record<SocialKeys, string>>;
};

type TenantSettingsPatch = {
  displayName?: string | null;
  contact?: { email?: string | null; website?: string | null };
  social?: Partial<Record<SocialKeys, string | null>>;
};

interface Props {
  initial: TenantSettings;
}

function normalizeUrl(input: string | undefined): string | undefined {
  if (!input) return undefined;
  const trimmed = input.trim();
  if (!trimmed) return undefined;
  if (/^https?:\/\//i.test(trimmed)) return trimmed;
  if (/^[\w.-]+\.[a-z]{2,}$/i.test(trimmed)) return `https://${trimmed}`;
  return trimmed;
}

export default function TenantSettingsForm({ initial }: Props) {
  const [fields, setFields] = useState<TenantSettings>(initial);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  function update<K extends keyof TenantSettings>(key: K, val: TenantSettings[K]) {
    setFields((f) => ({ ...f, [key]: val }));
  }

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError(null);
    setSuccess(null);
    try {
      const patch: TenantSettingsPatch = {};
      if (typeof fields.displayName === 'string') {
        const v = fields.displayName.trim();
        if (v || initial.displayName) patch.displayName = v || null;
      }
      if (fields.contact || initial.contact) {
        const c: { email?: string | null; website?: string | null } = {};
        const nextEmail = (fields.contact?.email || '').trim();
        const nextWebsite = normalizeUrl(fields.contact?.website);
        if (nextEmail || initial.contact?.email) c.email = nextEmail || null;
        if (nextWebsite || initial.contact?.website) c.website = nextWebsite || null;
        if (Object.keys(c).length) patch.contact = c;
      }
      if (fields.social || initial.social) {
        const s: Partial<Record<SocialKeys, string | null>> = {};
        for (const k of ['twitter', 'facebook', 'instagram', 'youtube', 'linkedin'] as const) {
          const raw = fields.social?.[k];
          const normalized = normalizeUrl(raw);
          const had = initial.social?.[k];
          if (normalized || had) s[k] = normalized || null;
        }
        if (Object.keys(s).length) patch.social = s;
      }

      const res = await fetch('/api-proxy/tenants/settings', {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(patch),
      });
      if (!res.ok) {
        setError(`Update failed (${res.status}).`);
      } else {
        setSuccess('Settings updated.');
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unexpected error');
    } finally {
      setSaving(false);
    }
  }

  const inputCls =
    'w-full rounded-md border border-line bg-[var(--color-surface)] px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-[var(--color-accent-600)]';
  const fieldsetCls = 'grid gap-4 sm:grid-cols-2';

  return (
    <form
      onSubmit={onSubmit}
      className="space-y-6"
      aria-describedby={error ? 'tenant-error' : undefined}
    >
      <fieldset className={fieldsetCls} disabled={saving}>
        <div className="sm:col-span-2">
          <label htmlFor="org-display" className="block text-xs font-medium mb-1">
            Organization Display Name
          </label>
          <input
            id="org-display"
            className={inputCls}
            value={fields.displayName || ''}
            onChange={(e) => update('displayName', e.target.value)}
          />
        </div>
        <div>
          <label htmlFor="org-email" className="block text-xs font-medium mb-1">
            Contact Email
          </label>
          <input
            id="org-email"
            className={inputCls}
            value={fields.contact?.email || ''}
            onChange={(e) =>
              update('contact', { ...(fields.contact || {}), email: e.target.value })
            }
          />
        </div>
        <div>
          <label htmlFor="org-website" className="block text-xs font-medium mb-1">
            Website
          </label>
          <input
            id="org-website"
            className={inputCls}
            value={fields.contact?.website || ''}
            onChange={(e) =>
              update('contact', { ...(fields.contact || {}), website: e.target.value })
            }
          />
        </div>
      </fieldset>

      <div>
        <h3 className="text-sm font-semibold mb-2">Social Links</h3>
        <fieldset className={fieldsetCls} disabled={saving}>
          {(['twitter', 'facebook', 'instagram', 'youtube', 'linkedin'] as const).map((k) => (
            <div key={k}>
              <label htmlFor={`org-${k}`} className="block text-xs font-medium mb-1 capitalize">
                {k}
              </label>
              <input
                id={`org-${k}`}
                className={inputCls}
                value={fields.social?.[k] || ''}
                onChange={(e) =>
                  update('social', { ...(fields.social || {}), [k]: e.target.value })
                }
              />
            </div>
          ))}
        </fieldset>
      </div>

      {error && (
        <div
          id="tenant-error"
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
}
