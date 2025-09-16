'use client';

import React from 'react';

type Props = { onUploaded?: (url: string) => void };

export function AvatarUpload({ onUploaded }: Props) {
  const [file, setFile] = React.useState<File | null>(null);
  const [error, setError] = React.useState<string | null>(null);
  const [submitting, setSubmitting] = React.useState(false);
  const [preview, setPreview] = React.useState<string | null>(null);

  function onChange(e: React.ChangeEvent<HTMLInputElement>) {
    setError(null);
    const f = e.target.files?.[0] ?? null;
    if (!f) {
      setFile(null);
      setPreview(null);
      return;
    }
    if (!['image/png', 'image/jpeg', 'image/webp'].includes(f.type)) {
      setError('Only PNG, JPEG, or WebP images are allowed.');
      return;
    }
    if (f.size > 2 * 1024 * 1024) {
      setError('File is too large (max 2MB).');
      return;
    }
    setFile(f);
    setPreview(URL.createObjectURL(f));
  }

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!file) return;
    setSubmitting(true);
    try {
      const fd = new FormData();
      fd.set('file', file);
      const res = await fetch('/api-proxy/users/me/avatar', { method: 'POST', body: fd });
      if (!res.ok) {
        const text = await res.text();
        setError(`Upload failed (${res.status}): ${text || 'Unknown error'}`);
        return;
      }
      const data = (await res.json()) as { avatar?: { url?: string } };
      const url = data?.avatar?.url;
      if (url) {
        const cacheBusted = `${url}${url.includes('?') ? '&' : '?'}v=${Date.now()}`;
        // Fire global event so other components (e.g., ProfileMenu) can update without reload
        if (typeof window !== 'undefined') {
          window.dispatchEvent(new CustomEvent('avatar-updated', { detail: { url: cacheBusted } }));
        }
        onUploaded?.(cacheBusted);
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Upload failed');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form onSubmit={onSubmit} className="flex items-center gap-3">
      <input
        aria-label="Choose avatar image"
        type="file"
        accept="image/png,image/jpeg,image/webp"
        onChange={onChange}
        disabled={submitting}
      />
      <button
        className="rounded bg-primary px-3 py-1.5 text-sm text-primary-foreground disabled:opacity-50"
        type="submit"
        disabled={!file || submitting}
      >
        {submitting ? 'Uploading…' : 'Upload'}
      </button>
      {preview ? (
        <img
          src={preview}
          alt="Preview"
          className="h-10 w-10 rounded-full border border-line object-cover"
        />
      ) : null}
      {error ? (
        <div role="alert" className="text-sm text-red-500">
          {error}
        </div>
      ) : null}
    </form>
  );
}
