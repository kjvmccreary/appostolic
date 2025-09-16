'use client';
import * as React from 'react';

export default function ForgotPasswordPage() {
  const [email, setEmail] = React.useState('');
  const [pending, setPending] = React.useState(false);
  const [status, setStatus] = React.useState<string | null>(null);

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!email) return;
    setPending(true);
    setStatus(null);
    try {
      const res = await fetch('/api-proxy/auth/forgot-password', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ email }),
      });
      setStatus(
        res.ok
          ? 'If the email exists, a password reset link has been sent.'
          : 'Request received. Please try again shortly.',
      );
    } catch {
      setStatus('Something went wrong. Please try again.');
    } finally {
      setPending(false);
    }
  };

  return (
    <main className="mx-auto max-w-sm p-6">
      <h1 className="mb-2 text-2xl font-semibold">Forgot password</h1>
      <p className="mb-6 text-sm text-muted">
        Enter your email and we’ll send you a link to reset your password.
      </p>
      <form onSubmit={onSubmit} className="space-y-4" aria-describedby="fp-help">
        <div>
          <label htmlFor="email" className="mb-1 block text-sm font-medium">
            Email
          </label>
          <input
            id="email"
            name="email"
            type="email"
            autoComplete="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className="w-full rounded-md border border-line bg-[var(--color-surface)] px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-[var(--color-accent-600)]"
          />
          <p id="fp-help" className="mt-1 text-xs text-muted">
            We’ll never share your email.
          </p>
        </div>
        <button
          type="submit"
          disabled={pending}
          className="h-9 w-full rounded-md bg-[var(--color-accent-600)] px-3 text-sm font-medium text-white disabled:opacity-60"
        >
          {pending ? 'Sending…' : 'Send reset link'}
        </button>
      </form>
      {status && (
        <p className="mt-4 text-sm" role="status" aria-live="polite">
          {status}
        </p>
      )}
      <p className="mt-6 text-sm">
        <a href="/login" className="text-[var(--color-accent-600)] hover:underline">
          Back to sign in
        </a>
      </p>
    </main>
  );
}
