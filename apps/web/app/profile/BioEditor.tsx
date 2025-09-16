'use client';
import React, { useState } from 'react';

/**
 * BioEditor (UPROF-10)
 * Purpose: Allow a user to edit their profile bio (markdown source) and submit a minimal
 * JSON merge patch to the profile endpoint via the web proxy. The API is expected to
 * sanitize and (optionally) render markdown → sanitized HTML on read; here we only send
 * the raw markdown content and a format indicator.
 *
 * Semantics:
 * - If the user clears the bio (empty string) and clicks Save, we send `{ "profile": { "bio": null } }`
 *   so the server removes the field (null = clear) respecting existing deep merge rules.
 * - If unchanged, the Save button stays disabled.
 * - Basic client-side length guard (e.g., 4000 chars) to prevent overly large payloads; server remains authority.
 * - Accessible: textarea labeled, character count announced via `aria-describedby`, status region for success/error.
 */

interface BioEditorProps {
  initial?: { format?: string; content?: string } | null;
  maxChars?: number;
  onSaved?: (next: { format: string; content: string } | null) => void;
}

export const BioEditor: React.FC<BioEditorProps> = ({ initial, maxChars = 4000, onSaved }) => {
  const [value, setValue] = useState(initial?.content ?? '');
  const [saving, setSaving] = useState(false);
  const [status, setStatus] = useState<'idle' | 'success' | 'error'>('idle');
  const [error, setError] = useState<string | null>(null);

  const dirty = value !== (initial?.content ?? '');
  const overLimit = value.length > maxChars;

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!dirty || overLimit) return;
    setSaving(true);
    setStatus('idle');
    setError(null);
    try {
      // Build minimal merge patch body for profile.bio
      let body: Record<string, unknown>;
      if (value.trim() === '') {
        body = { profile: { bio: null } }; // clearing bio
      } else {
        body = { profile: { bio: { format: 'markdown', content: value } } };
      }
      const res = await fetch('/api-proxy/users/me', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      if (res.ok) {
        setStatus('success');
        if (value.trim() === '') {
          onSaved?.(null);
        } else {
          onSaved?.({ format: 'markdown', content: value });
        }
      } else {
        setStatus('error');
        setError(`Failed to save bio (status ${res.status})`);
      }
    } catch (err) {
      setStatus('error');
      const msg = err instanceof Error ? err.message : 'Unexpected error saving bio';
      setError(msg);
    } finally {
      setSaving(false);
    }
  }

  function handleClear() {
    if (!value) return;
    setValue('');
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3" aria-labelledby="bio-heading">
      <div>
        <h2 id="bio-heading" className="text-lg font-medium">
          Bio
        </h2>
        <p className="text-sm text-neutral-500" id="bio-help">
          Write a short introduction (Markdown supported). {maxChars} characters max.
        </p>
      </div>
      <div>
        <label htmlFor="bio" className="block text-sm font-medium mb-1">
          Bio (Markdown)
        </label>
        <textarea
          id="bio"
          name="bio"
          className="w-full border rounded p-2 text-sm resize-vertical min-h-[140px]"
          value={value}
          onChange={(e) => setValue(e.target.value)}
          aria-describedby="bio-help bio-count"
          // Using aria-invalid boolean attribute (present only when true)
          {...(overLimit ? { 'aria-invalid': true } : {})}
          disabled={saving}
        />
        <div className="flex items-center justify-between mt-1">
          <span
            id="bio-count"
            className={`text-xs ${overLimit ? 'text-red-600' : 'text-neutral-500'}`}
          >
            {value.length}/{maxChars}
          </span>
          <button
            type="button"
            onClick={handleClear}
            className="text-xs underline disabled:opacity-40"
            disabled={!value || saving}
          >
            Clear
          </button>
        </div>
        {overLimit && (
          <div role="alert" className="text-xs text-red-600 mt-1">
            Bio is too long. Please shorten.
          </div>
        )}
      </div>
      <div className="flex items-center gap-3">
        <button
          type="submit"
          className="px-3 py-1 rounded bg-blue-600 text-white text-sm disabled:opacity-50"
          disabled={!dirty || overLimit || saving}
        >
          {saving ? 'Saving…' : 'Save Bio'}
        </button>
        {status === 'success' && (
          <span role="status" className="text-sm text-green-600">
            Bio saved
          </span>
        )}
        {status === 'error' && (
          <span role="alert" className="text-sm text-red-600">
            {error || 'Failed to save bio'}
          </span>
        )}
      </div>
    </form>
  );
};

export default BioEditor;
