'use client';
import * as React from 'react';

export default function ResetPasswordPage() {
  // Read token from query string once on mount; keep hidden in form, not editable
  const [token, setToken] = React.useState('');
  const [pw, setPw] = React.useState('');
  const [pw2, setPw2] = React.useState('');
  const [pending, setPending] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [success, setSuccess] = React.useState<string | null>(null);

  React.useEffect(() => {
    try {
      const params = new URLSearchParams(window.location.search);
      const t = params.get('token') || '';
      setToken(t);
    } catch {
      // ignore
    }
  }, []);

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setSuccess(null);
    if (!token) {
      setError('Missing or invalid reset token. Please use the link from your email.');
      return;
    }
    if (!pw || pw.length < 8) {
      setError('Password must be at least 8 characters.');
      return;
    }
    if (pw !== pw2) {
      setError('Passwords do not match.');
      return;
    }
    setPending(true);
    try {
      const res = await fetch('/api-proxy/auth/reset-password', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ token, newPassword: pw }),
      });
      if (res.ok) {
        setSuccess('Password reset. You can now sign in.');
      } else {
        setError('Reset failed: invalid or expired token.');
      }
    } catch {
      setError('Something went wrong. Please try again.');
    } finally {
      setPending(false);
    }
  };

  return (
    <main className="mx-auto max-w-sm p-6">
      <h1 className="mb-2 text-2xl font-semibold">Reset password</h1>
      <p className="mb-6 text-sm text-muted">Enter your new password below.</p>
      <form onSubmit={onSubmit} className="space-y-4" aria-describedby="rp-help">
        {/* Keep token hidden; it comes from the email link */}
        <input type="hidden" name="token" value={token} readOnly />
        <div>
          <label htmlFor="pw" className="mb-1 block text-sm font-medium">
            New password
          </label>
          <input
            id="pw"
            name="pw"
            type="password"
            autoComplete="new-password"
            required
            value={pw}
            onChange={(e) => setPw(e.target.value)}
            className="w-full rounded-md border border-line bg-[var(--color-surface)] px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-[var(--color-accent-600)]"
          />
        </div>
        <div>
          <label htmlFor="pw2" className="mb-1 block text-sm font-medium">
            Confirm new password
          </label>
          <input
            id="pw2"
            name="pw2"
            type="password"
            autoComplete="new-password"
            required
            value={pw2}
            onChange={(e) => setPw2(e.target.value)}
            className="w-full rounded-md border border-line bg-[var(--color-surface)] px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-[var(--color-accent-600)]"
          />
          <p id="rp-help" className="mt-1 text-xs text-muted">
            Password must be at least 8 characters.
          </p>
        </div>
        <button
          type="submit"
          disabled={pending}
          className="h-9 w-full rounded-md bg-[var(--color-accent-600)] px-3 text-sm font-medium text-white disabled:opacity-60"
        >
          {pending ? 'Resetting…' : 'Reset password'}
        </button>
      </form>
      {error && (
        <p className="mt-4 text-sm text-red-600" role="alert">
          {error}
        </p>
      )}
      {success && (
        <p className="mt-4 text-sm" role="status" aria-live="polite">
          {success}{' '}
          <a href="/login" className="text-[var(--color-accent-600)] hover:underline">
            Sign in
          </a>
        </p>
      )}
    </main>
  );
}
