'use client';
import React, { useState, FormEvent } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { signIn } from 'next-auth/react';

export default function SignupClient() {
  const router = useRouter();
  const params = useSearchParams();
  const next = params.get('next') || '/select-tenant';
  // Accept multiple param aliases for compatibility: invite (emails), inviteToken, token
  const inviteToken =
    params.get('invite') || params.get('inviteToken') || params.get('token') || '';

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      const body: Record<string, unknown> = { email, password };
      if (inviteToken) body.inviteToken = inviteToken;
      // Call same-origin proxy to avoid CORS
      const res = await fetch(`/api-proxy/auth/signup`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(body),
      });
      if (!res.ok) {
        // Read body once; prefer JSON.error when available, else raw text
        let message = 'Signup failed';
        const raw = await res.text().catch(() => '');
        if (raw) {
          try {
            const data = JSON.parse(raw);
            if (data?.error && typeof data.error === 'string') message = data.error;
            else message = typeof data === 'string' ? data : JSON.stringify(data);
          } catch {
            message = raw;
          }
        }
        setError(message);
        return;
      }
      // Auto sign-in with entered credentials, then move to next step
      const signInRes = await signIn('credentials', {
        email,
        password,
        redirect: false,
        callbackUrl: next,
      });
      if (signInRes?.error) {
        // Fallback: send to login with next
        router.replace(`/login?next=${encodeURIComponent(next)}`);
        return;
      }
      router.replace(next);
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className="mx-auto max-w-md p-6">
      <h1 className="text-2xl font-semibold mb-4">Create your account</h1>
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
        <label htmlFor="password">Password</label>
        <input
          id="password"
          name="password"
          type="password"
          placeholder="••••••••"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />
        {error && (
          <p role="alert" className="text-red-600 text-sm">
            {error}
          </p>
        )}
        <button type="submit" disabled={loading}>
          {loading ? 'Creating…' : 'Sign up'}
        </button>
      </form>
    </main>
  );
}
