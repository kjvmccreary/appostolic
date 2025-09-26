'use client';
import React, { useEffect, useState } from 'react';

interface GuardrailsInitial {
  denominationAlignment?: string;
  favoriteAuthors?: string[];
  favoriteBooks?: string[];
  notes?: string;
  lessonFormat?: string; // preferences.lessonFormat
  denominations?: string[]; // presets.denominations
}

interface TenantGuardrailsFormProps {
  initial: GuardrailsInitial;
  presets?: { id: string; name: string; notes?: string }[];
}

const LESSON_FORMATS = ['Engaging', 'Monologue', 'Games', 'Discussion', 'Interactive'] as const;

type ChipKind = 'favoriteAuthors' | 'favoriteBooks';

interface GuardrailsPatch {
  denominationAlignment?: string;
  favoriteAuthors?: string[];
  favoriteBooks?: string[];
  notes?: string;
}
interface PreferencesPatch {
  lessonFormat?: string;
}
interface SettingsPatch {
  guardrails?: GuardrailsPatch;
  preferences?: PreferencesPatch;
  presets?: { denominations?: string[] };
}

/**
 * TenantGuardrailsForm
 * Purpose: Manage organization-level doctrinal guardrails, preferences, and denomination presets.
 * Sends a JSON merge patch to /api-proxy/tenants/settings with top-level keys (guardrails, preferences, presets).
 */
export const TenantGuardrailsForm: React.FC<TenantGuardrailsFormProps> = ({ initial, presets }) => {
  const [denominationAlignment, setDenominationAlignment] = useState(
    initial.denominationAlignment || '',
  );
  const [favoriteAuthors, setFavoriteAuthors] = useState<string[]>(initial.favoriteAuthors || []);
  const [favoriteBooks, setFavoriteBooks] = useState<string[]>(initial.favoriteBooks || []);
  const [denominations, setDenominations] = useState<string[]>(initial.denominations || []);
  const [notes, setNotes] = useState(initial.notes || '');
  const [lessonFormat, setLessonFormat] = useState(initial.lessonFormat || '');
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [search, setSearch] = useState('');

  useEffect(() => {
    setDenominationAlignment(initial.denominationAlignment || '');
    setFavoriteAuthors([...(initial.favoriteAuthors || [])]);
    setFavoriteBooks([...(initial.favoriteBooks || [])]);
    setDenominations([...(initial.denominations || [])]);
    setNotes(initial.notes || '');
    setLessonFormat(initial.lessonFormat || '');
    setError(null);
    setSuccess(null);
    setSearch('');
  }, [initial]);

  function addChip(kind: ChipKind, value: string) {
    const v = value.trim();
    if (!v) return;
    setSuccess(null);
    if (kind === 'favoriteAuthors') {
      if (!favoriteAuthors.includes(v)) setFavoriteAuthors([...favoriteAuthors, v]);
    } else {
      if (!favoriteBooks.includes(v)) setFavoriteBooks([...favoriteBooks, v]);
    }
  }

  function removeChip(kind: ChipKind, value: string) {
    if (kind === 'favoriteAuthors') setFavoriteAuthors(favoriteAuthors.filter((x) => x !== value));
    else setFavoriteBooks(favoriteBooks.filter((x) => x !== value));
  }

  function addDenomination(id: string) {
    if (!denominations.includes(id)) {
      setDenominations([...denominations, id]);
      if (!denominationAlignment) {
        const preset = presets?.find((p) => p.id === id);
        if (preset) setDenominationAlignment(preset.name);
      }
      setSuccess(null);
    }
  }

  function removeDenomination(id: string) {
    setDenominations(denominations.filter((d) => d !== id));
    setSuccess(null);
  }

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setPending(true);
    setError(null);
    setSuccess(null);
    try {
      const guardrails: GuardrailsPatch = {};
      if (denominationAlignment) guardrails.denominationAlignment = denominationAlignment.trim();
      guardrails.favoriteAuthors = favoriteAuthors;
      guardrails.favoriteBooks = favoriteBooks;
      guardrails.notes = notes ? notes.trim() : '';
      const preferences: PreferencesPatch = {};
      if (lessonFormat) preferences.lessonFormat = lessonFormat;
      const patch: SettingsPatch = { guardrails, presets: { denominations } };
      if (Object.keys(preferences).length) patch.preferences = preferences;

      const res = await fetch('/api-proxy/tenants/settings', {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(patch),
      });
      if (!res.ok) setError(`Update failed (${res.status}).`);
      else setSuccess('Organization guardrails updated.');
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Unexpected error';
      setError(msg);
    } finally {
      setPending(false);
    }
  }

  const chipCls = 'inline-flex items-center gap-1 rounded bg-muted px-2 py-0.5 text-xs';
  const chipBtnCls = 'text-muted-foreground hover:text-foreground focus:outline-none';
  const inputCls =
    'w-full rounded-md border border-line bg-[var(--color-surface)] px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-[var(--color-accent-600)]';
  const pillBtnCls =
    'cursor-pointer rounded border border-line px-2 py-1 text-xs hover:bg-accent flex items-center gap-1 focus:outline-none focus:ring-2 focus:ring-[var(--color-accent-600)]';

  function Chips({ kind, items }: { kind: ChipKind; items: string[] }) {
    const [draft, setDraft] = useState('');
    return (
      <div className="space-y-2">
        <div className="flex flex-wrap gap-2" data-testid={`${kind}-chips`}>
          {items.map((i) => (
            <span key={i} className={chipCls}>
              {i}
              <button
                type="button"
                className={chipBtnCls}
                aria-label={`Remove ${i}`}
                onClick={() => removeChip(kind, i)}
              >
                ×
              </button>
            </span>
          ))}
        </div>
        <input
          aria-label={`Add ${kind === 'favoriteAuthors' ? 'author' : 'book'}`}
          className={inputCls}
          placeholder={
            kind === 'favoriteAuthors' ? 'Add author and press Enter' : 'Add book and press Enter'
          }
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault();
              addChip(kind, draft);
              setDraft('');
            }
          }}
          disabled={pending}
        />
      </div>
    );
  }

  const filteredPresets = (presets || []).filter((p) => {
    if (!search.trim()) return !denominations.includes(p.id);
    const s = search.toLowerCase();
    return (
      !denominations.includes(p.id) &&
      (p.name.toLowerCase().includes(s) ||
        p.id.toLowerCase().includes(s) ||
        (p.notes || '').toLowerCase().includes(s))
    );
  });

  return (
    <form
      onSubmit={onSubmit}
      className="space-y-6"
      aria-describedby={error ? 'tgr-error' : undefined}
    >
      <div className="grid gap-4 sm:grid-cols-2">
        <div className="sm:col-span-2">
          <label className="block text-xs font-medium mb-1" htmlFor="tgr-denominations-search">
            Denominations
          </label>
          <div className="space-y-3">
            <div className="flex flex-wrap gap-2" data-testid="denomination-chips">
              {denominations.map((id) => {
                const preset = presets?.find((p) => p.id === id);
                const label = preset ? preset.name : id;
                return (
                  <span key={id} className={chipCls} aria-label={label} title={label}>
                    {label}
                    <button
                      type="button"
                      className={chipBtnCls}
                      aria-label={`Remove ${label}`}
                      onClick={() => removeDenomination(id)}
                    >
                      ×
                    </button>
                  </span>
                );
              })}
              {denominations.length === 0 && (
                <span className="text-xs text-muted-foreground">No denominations selected.</span>
              )}
            </div>
            <input
              id="tgr-denominations-search"
              placeholder="Search denominations..."
              className={inputCls}
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              disabled={pending}
            />
            <ul
              className="flex flex-wrap gap-2"
              aria-label="Available denominations"
              data-testid="denomination-options"
            >
              {filteredPresets.slice(0, 24).map((p) => (
                <li key={p.id}>
                  <button
                    type="button"
                    className={pillBtnCls}
                    onClick={() => addDenomination(p.id)}
                    disabled={pending}
                    aria-label={`Add ${p.name}`}
                    title={p.notes || p.name}
                  >
                    <span>{p.name}</span>
                    <span className="text-muted-foreground">+</span>
                  </button>
                </li>
              ))}
              {filteredPresets.length === 0 && (
                <li className="text-xs text-muted-foreground">No matches.</li>
              )}
            </ul>
            <p className="text-[11px] text-muted-foreground" id="tgr-denominations-help">
              Add denominations that represent your organization. Alignment auto-fills when adding
              the first if empty.
            </p>
          </div>
        </div>
        <div className="sm:col-span-2">
          <label htmlFor="tgr-denomination" className="block text-xs font-medium mb-1">
            Denomination Alignment
          </label>
          <input
            id="tgr-denomination"
            className={inputCls}
            value={denominationAlignment}
            onChange={(e) => setDenominationAlignment(e.target.value)}
            disabled={pending}
          />
        </div>
        <div className="sm:col-span-2">
          <label className="block text-xs font-medium mb-1">Favorite Authors</label>
          <Chips kind="favoriteAuthors" items={favoriteAuthors} />
        </div>
        <div className="sm:col-span-2">
          <label className="block text-xs font-medium mb-1">Favorite Books</label>
          <Chips kind="favoriteBooks" items={favoriteBooks} />
        </div>
        <div className="sm:col-span-2">
          <label htmlFor="tgr-notes" className="block text-xs font-medium mb-1">
            Notes
          </label>
          <textarea
            id="tgr-notes"
            className={inputCls + ' h-24'}
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            disabled={pending}
          />
        </div>
        <div>
          <label htmlFor="tgr-lesson-format" className="block text-xs font-medium mb-1">
            Preferred Lesson Format
          </label>
          <select
            id="tgr-lesson-format"
            className={inputCls}
            value={lessonFormat}
            onChange={(e) => setLessonFormat(e.target.value)}
            disabled={pending}
          >
            <option value="">(None)</option>
            {LESSON_FORMATS.map((f) => (
              <option key={f} value={f}>
                {f}
              </option>
            ))}
          </select>
        </div>
      </div>

      {error && (
        <div
          id="tgr-error"
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
          disabled={pending}
          className="inline-flex h-9 items-center rounded-md bg-[var(--color-accent-600)] px-3 text-sm font-medium text-white disabled:opacity-60"
        >
          {pending ? 'Saving…' : 'Save Guardrails'}
        </button>
      </div>
    </form>
  );
};

export default TenantGuardrailsForm;
