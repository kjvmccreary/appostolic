'use client';
import React, { useEffect, useMemo, useState } from 'react';
import Markdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import remarkBreaks from 'remark-breaks';

interface BioContent {
  format?: string;
  content?: string;
}

interface TenantBioEditorProps {
  initial?: BioContent | null;
  maxChars?: number;
}

/**
 * TenantBioEditor
 * Purpose: Edit the organization bio (markdown). Sends minimal merge patch to /api-proxy/tenants/settings.
 * Semantics: empty string -> { bio: null } to clear. Otherwise { bio: { format: 'markdown', content }}.
 */
export const TenantBioEditor: React.FC<TenantBioEditorProps> = ({ initial, maxChars = 4000 }) => {
  const [baseline, setBaseline] = useState<BioContent | null>(initial ?? null);
  const [value, setValue] = useState(initial?.content ?? '');
  const [saving, setSaving] = useState(false);
  const [status, setStatus] = useState<'idle' | 'success' | 'error'>('idle');
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState<0 | 1>(0);
  const dirty = value !== (baseline?.content ?? '');
  const overLimit = value.length > maxChars;
  const previewContent = useMemo(() => value, [value]);

  useEffect(() => {
    setBaseline(initial ?? null);
    setValue(initial?.content ?? '');
    setStatus('idle');
    setError(null);
    setTab(0);
  }, [initial]);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!dirty || overLimit) return;
    setSaving(true);
    setStatus('idle');
    setError(null);
    try {
      const body: Record<string, unknown> = {};
      const trimmed = value.trim();
      const baselineContent = baseline?.content ?? '';
      if (trimmed === '' && baselineContent !== '') body.bio = null;
      else if (trimmed !== '' && trimmed !== baselineContent)
        body.bio = { format: 'markdown', content: value };
      if (!Object.keys(body).length) {
        setSaving(false);
        return;
      }
      const res = await fetch('/api-proxy/tenants/settings', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      if (res.ok) {
        setStatus('success');
        if (body.bio === null) setBaseline(null);
        else if (body.bio) setBaseline({ format: 'markdown', content: value });
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

  return (
    <form onSubmit={onSubmit} className="space-y-3" aria-labelledby="tenant-bio-heading">
      <div>
        <div id="tenant-bio-heading" className="text-sm font-medium">
          Organization Bio
        </div>
        <p id="tenant-bio-help" className="text-xs text-muted-foreground">
          Write a short description for your organization. Supports Markdown. {maxChars} characters
          max.
        </p>
      </div>
      <div className="rounded border border-line bg-[var(--color-surface)]">
        <div className="flex text-xs" aria-label="Bio editor tabs">
          <button
            type="button"
            className={`px-3 py-2 border-b ${tab === 0 ? 'border-accent text-foreground' : 'border-transparent text-muted-foreground'}`}
            onClick={() => setTab(0)}
          >
            Write
          </button>
          <button
            type="button"
            className={`px-3 py-2 border-b ${tab === 1 ? 'border-accent text-foreground' : 'border-transparent text-muted-foreground'}`}
            onClick={() => setTab(1)}
          >
            Preview
          </button>
        </div>
        {tab === 0 && (
          <div id="tenant-bio-panel-write" role="tabpanel" className="p-3 space-y-2">
            <textarea
              id="tenant-bio"
              className="w-full rounded-md border border-line bg-[var(--color-surface)] px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-[var(--color-accent-600)] min-h-[160px]"
              placeholder="Write your organization bio in Markdown..."
              value={value}
              onChange={(e) => setValue(e.target.value)}
              aria-describedby="tenant-bio-help tenant-bio-count"
              disabled={saving}
            />
            <div className="flex items-center justify-between">
              <span
                id="tenant-bio-count"
                className={`text-[11px] ${overLimit ? 'text-red-600' : 'text-muted-foreground'}`}
              >
                {value.length}/{maxChars}
              </span>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  className="text-xs text-muted-foreground hover:text-foreground"
                  aria-label="Copy bio"
                  onClick={() => navigator.clipboard.writeText(value)}
                  disabled={!value}
                >
                  Copy
                </button>
                <button
                  type="button"
                  className="text-xs text-muted-foreground hover:text-foreground"
                  aria-label="Clear bio"
                  onClick={() => setValue('')}
                  disabled={!value || saving}
                >
                  Clear
                </button>
              </div>
            </div>
          </div>
        )}
        {tab === 1 && (
          <div id="tenant-bio-panel-preview" role="tabpanel" className="p-3">
            {previewContent.trim() ? (
              <div className="prose prose-sm max-w-none">
                <Markdown remarkPlugins={[remarkGfm, remarkBreaks]}>{previewContent}</Markdown>
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">Nothing to preview yet.</p>
            )}
          </div>
        )}
      </div>
      <div className="flex items-center gap-3">
        <button
          type="submit"
          className="inline-flex h-9 items-center rounded-md bg-[var(--color-accent-600)] px-3 text-sm font-medium text-white disabled:opacity-60"
          disabled={!dirty || overLimit || saving}
        >
          {saving ? 'Saving…' : 'Save Bio'}
        </button>
        {status === 'success' && (
          <span role="status" className="text-sm text-green-700">
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

export default TenantBioEditor;
