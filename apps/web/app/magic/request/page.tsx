'use client';
import React, { useState, FormEvent } from 'react';

export default function MagicRequestPage() {
  const [email, setEmail] = useState('');
  const [submitted, setSubmitted] = useState(false);
  const [loading, setLoading] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setLoading(true);
    try {
      const res = await fetch('/api-proxy/auth/magic/request', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ email }),
      });
      if (!res.ok) {
        // Do not enumerate. Show a generic message either way; log for local dev.
        const txt = await res.text().catch(() => '');
        console.warn('Magic link request non-200:', res.status, txt);
      }
      setSubmitted(true);
    } catch (err) {
      console.error(err);
      setSubmitted(true);
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className="mx-auto max-w-sm p-6">
      <h1 className="mb-2 text-2xl font-semibold">Sign in with magic link</h1>
      <p className="mb-6 text-sm text-muted">
        Enter your email and we’ll send you a one‑time sign‑in link.
      </p>
      {submitted ? (
        <div className="space-y-3">
          <p>
            If an account exists for <b>{email}</b>, we’ve sent a sign‑in link. Please check your
            email.
          </p>
          <p className="text-sm text-muted">
            The link expires in about 15 minutes. You may request a new one if needed.
          </p>
          <p className="text-sm">
            <a href="/login" className="text-[var(--color-accent-600)] hover:underline">
              Back to sign in
            </a>
          </p>
        </div>
      ) : (
        <form onSubmit={onSubmit} className="space-y-4" aria-describedby="ml-help">
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
              placeholder="you@example.com"
              className="w-full rounded-md border border-line bg-[var(--color-surface)] px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-[var(--color-accent-600)]"
            />
            <p id="ml-help" className="mt-1 text-xs text-muted">
              We’ll never share your email.
            </p>
          </div>
          <button
            type="submit"
            disabled={loading}
            className="h-9 w-full rounded-md bg-[var(--color-accent-600)] px-3 text-sm font-medium text-white disabled:opacity-60"
          >
            {loading ? 'Sending…' : 'Send magic link'}
          </button>
          <p className="text-sm">
            <a href="/login" className="text-[var(--color-accent-600)] hover:underline">
              Back to sign in
            </a>
          </p>
        </form>
      )}
    </main>
  );
}
