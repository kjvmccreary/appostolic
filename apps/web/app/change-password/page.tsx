'use client';
import * as React from 'react';

function passwordStrength(pw: string): { score: number; label: string } {
  let score = 0;
  if (pw.length >= 8) score++;
  if (/[A-Z]/.test(pw)) score++;
  if (/[a-z]/.test(pw)) score++;
  if (/\d/.test(pw)) score++;
  if (/[^A-Za-z0-9]/.test(pw)) score++;
  const labels = ['Very Weak', 'Weak', 'Fair', 'Good', 'Strong', 'Strong+'];
  return { score, label: labels[score] || 'Very Weak' };
}

export default function ChangePasswordPage() {
  const [current, setCurrent] = React.useState('');
  const [pw, setPw] = React.useState('');
  const [confirm, setConfirm] = React.useState('');
  const [error, setError] = React.useState<string | null>(null);
  const [status, setStatus] = React.useState<string | null>(null);
  const [submitting, setSubmitting] = React.useState(false);

  const strength = passwordStrength(pw);
  const strengthOk = pw.length >= 8 && /[A-Za-z]/.test(pw) && /\d/.test(pw);
  const confirmMatch = !pw || pw === confirm;

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setStatus(null);
    if (!strengthOk) {
      setError('Password must be at least 8 characters and include a letter and a digit.');
      return;
    }
    if (!confirmMatch) {
      setError('Passwords do not match.');
      return;
    }
    setSubmitting(true);
    try {
      const res = await fetch('/api-proxy/users/me/password', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ currentPassword: current, newPassword: pw }),
      });
      if (res.status === 204) {
        setStatus('Password changed.');
        setCurrent('');
        setPw('');
        setConfirm('');
      } else if (res.status === 400) {
        setError('Current password is incorrect.');
      } else if (res.status === 422) {
        setError('Password rejected by server (weak).');
      } else if (res.status === 401) {
        setError('Unauthorized. Please sign in again.');
      } else {
        setError(`Unexpected error (${res.status}).`);
      }
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Unexpected error';
      setError(msg);
    } finally {
      setSubmitting(false);
    }
  }

  const inputCls = 'w-full rounded border border-line bg-transparent px-2 py-1 text-sm';
  const meterColors = [
    'bg-red-500',
    'bg-red-400',
    'bg-yellow-400',
    'bg-lime-400',
    'bg-green-500',
    'bg-green-600',
  ];
  const meterBars = Array.from({ length: 5 }, (_, i) =>
    i < strength.score ? meterColors[strength.score] : 'bg-border',
  );

  return (
    <main
      id="main"
      className="container max-w-md space-y-6 p-4"
      aria-labelledby="change-pw-heading"
    >
      <h1 id="change-pw-heading" className="text-xl font-semibold">
        Change Password
      </h1>
      <form
        onSubmit={onSubmit}
        className="space-y-5"
        aria-describedby={error ? 'pw-error' : undefined}
      >
        <div>
          <label htmlFor="cp-current" className="block text-xs font-medium mb-1">
            Current password
          </label>
          <input
            id="cp-current"
            type="password"
            className={inputCls}
            value={current}
            onChange={(e) => setCurrent(e.target.value)}
            required
            disabled={submitting}
            autoComplete="current-password"
          />
        </div>
        <div>
          <label htmlFor="cp-new" className="block text-xs font-medium mb-1">
            New password
          </label>
          <input
            id="cp-new"
            type="password"
            className={inputCls}
            value={pw}
            onChange={(e) => setPw(e.target.value)}
            required
            disabled={submitting}
            autoComplete="new-password"
            aria-describedby="pw-strength-hint"
          />
          <div className="mt-2 flex items-center gap-2" aria-live="polite" id="pw-strength-hint">
            <div className="flex gap-1" aria-hidden="true">
              {meterBars.map((cls, i) => (
                <span key={i} className={`h-1.5 w-6 rounded ${cls}`} />
              ))}
            </div>
            <span className="text-xs text-muted-foreground">{strength.label}</span>
          </div>
        </div>
        <div>
          <label htmlFor="cp-confirm" className="block text-xs font-medium mb-1">
            Confirm new password
          </label>
          <input
            id="cp-confirm"
            type="password"
            className={inputCls}
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
            required
            disabled={submitting}
            autoComplete="new-password"
          />
          {!confirmMatch && confirm && (
            <p className="mt-1 text-xs text-red-500" role="alert">
              Mismatch.
            </p>
          )}
        </div>
        {error && (
          <div
            id="pw-error"
            role="alert"
            className="rounded border border-red-400 bg-red-50 p-2 text-sm text-red-700"
          >
            {error}
          </div>
        )}
        {status && (
          <div
            role="status"
            className="rounded border border-green-400 bg-green-50 p-2 text-sm text-green-700"
          >
            {status}
          </div>
        )}
        <div>
          <button
            type="submit"
            disabled={submitting}
            className="inline-flex items-center rounded bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground disabled:opacity-50"
          >
            {submitting ? 'Saving…' : 'Save Password'}
          </button>
        </div>
      </form>
      <p className="text-xs text-muted-foreground">
        Password must be 8+ chars and contain a letter and a digit. Symbols improve strength.
      </p>
    </main>
  );
}
