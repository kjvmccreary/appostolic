'use client';
import React, { useState } from 'react';

interface GuardrailsInitial {
  denominationAlignment?: string;
  favoriteAuthors?: string[];
  favoriteBooks?: string[];
  notes?: string;
  lessonFormat?: string; // preferences.lessonFormat
}

interface ProfileGuardrailsFormProps {
  initial: GuardrailsInitial;
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
interface ProfilePatch {
  guardrails?: GuardrailsPatch;
  preferences?: PreferencesPatch;
}
interface RootPatch {
  profile: ProfilePatch;
}

export const ProfileGuardrailsForm: React.FC<ProfileGuardrailsFormProps> = ({ initial }) => {
  const [denominationAlignment, setDenominationAlignment] = useState(
    initial.denominationAlignment || '',
  );
  const [favoriteAuthors, setFavoriteAuthors] = useState<string[]>(initial.favoriteAuthors || []);
  const [favoriteBooks, setFavoriteBooks] = useState<string[]>(initial.favoriteBooks || []);
  const [notes, setNotes] = useState(initial.notes || '');
  const [lessonFormat, setLessonFormat] = useState(initial.lessonFormat || '');
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

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

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setPending(true);
    setError(null);
    setSuccess(null);
    try {
      const guardrails: GuardrailsPatch = {};
      if (denominationAlignment) guardrails.denominationAlignment = denominationAlignment.trim();
      guardrails.favoriteAuthors = favoriteAuthors; // intentional full replacement
      guardrails.favoriteBooks = favoriteBooks; // intentional full replacement
      guardrails.notes = notes ? notes.trim() : '';
      const preferences: PreferencesPatch = {};
      if (lessonFormat) preferences.lessonFormat = lessonFormat;
      const patch: RootPatch = {
        profile: { guardrails, ...(Object.keys(preferences).length ? { preferences } : {}) },
      };
      const res = await fetch('/api-proxy/users/me', {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(patch),
      });
      if (!res.ok) setError(`Update failed (${res.status}).`);
      else setSuccess('Guardrails updated.');
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Unexpected error';
      setError(msg);
    } finally {
      setPending(false);
    }
  }

  const chipCls = 'inline-flex items-center gap-1 rounded bg-muted px-2 py-0.5 text-xs';
  const chipBtnCls = 'text-muted-foreground hover:text-foreground focus:outline-none';
  const inputCls = 'w-full rounded border border-line bg-transparent px-2 py-1 text-sm';

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

  return (
    <form
      onSubmit={onSubmit}
      className="space-y-6"
      aria-describedby={error ? 'guardrails-error' : undefined}
    >
      <div className="grid gap-4 sm:grid-cols-2">
        <div className="sm:col-span-2">
          <label htmlFor="pf-denomination" className="block text-xs font-medium mb-1">
            Denomination Alignment
          </label>
          <input
            id="pf-denomination"
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
          <label htmlFor="pf-notes" className="block text-xs font-medium mb-1">
            Notes
          </label>
          <textarea
            id="pf-notes"
            className={inputCls + ' h-24'}
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            disabled={pending}
          />
        </div>
        <div>
          <label htmlFor="pf-lesson-format" className="block text-xs font-medium mb-1">
            Preferred Lesson Format
          </label>
          <select
            id="pf-lesson-format"
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
          id="guardrails-error"
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
          className="inline-flex items-center rounded bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground disabled:opacity-50"
        >
          {pending ? 'Saving…' : 'Save Guardrails'}
        </button>
      </div>
    </form>
  );
};
