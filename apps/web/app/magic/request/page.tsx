'use client';
import React, { useState, FormEvent } from 'react';

export default function MagicRequestPage() {
  const [email, setEmail] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitted, setSubmitted] = useState(false);
  const [loading, setLoading] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      const res = await fetch('/api-proxy/auth/magic/request', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ email }),
      });
      if (!res.ok) {
        // Do not enumerate. Show a generic message on both success and failure.
        // Still log to console to aid local dev.
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
    <main className="mx-auto max-w-md p-6">
      <h1 className="text-2xl font-semibold mb-4">Sign in with Magic Link</h1>
      {submitted ? (
        <div className="space-y-3">
          <p>
            If an account exists for <b>{email}</b>, weve sent a sign-in link. Please check your
            email.
          </p>
          <p className="text-sm text-gray-600">
            The link expires in about 15 minutes. You may request a new one if needed.
          </p>
        </div>
      ) : (
        <form onSubmit={onSubmit} className="flex flex-col gap-3">
          <label htmlFor="email">Email</label>
          <input
            id="email"
            name="email"
            type="email"
            placeholder="you@example.com"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
          />
          {error && (
            <p role="alert" className="text-red-600 text-sm">
              {error}
            </p>
          )}
          <button type="submit" disabled={loading}>
            {loading ? 'Sending…' : 'Send Magic Link'}
          </button>
        </form>
      )}
    </main>
  );
}
