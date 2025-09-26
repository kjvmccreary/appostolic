'use client';
import React, { useState, FormEvent } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { signIn } from 'next-auth/react';
import styles from './styles.module.css';

export default function SignupClient() {
  const router = useRouter();
  const params = useSearchParams();
  const getParam = (key: string) => params?.get(key) ?? null;
  const next = getParam('next') ?? '/select-tenant';
  // Accept multiple param aliases for compatibility: invite (emails), inviteToken, token
  const inviteToken = getParam('invite') || getParam('inviteToken') || getParam('token') || '';

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
    <main className={styles.container}>
      <h1 className={styles.title}>Create your account</h1>
      {inviteToken && (
        <div className={styles.banner}>
          Already have an account?{' '}
          <a
            className="underline"
            href={`/login?next=${encodeURIComponent(`/invite/accept?token=${encodeURIComponent(inviteToken)}`)}`}
          >
            Log in
          </a>{' '}
          to accept your invite.
        </div>
      )}
      <form
        onSubmit={onSubmit}
        className={styles.form}
        aria-describedby={error ? 'signup-error' : undefined}
      >
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
        <p className={styles.helper} id="password-hint">
          Use a strong password; you can change it later in Account settings.
        </p>
        <label htmlFor="password">Password</label>
        <input
          id="password"
          name="password"
          type="password"
          placeholder="••••••••"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
          aria-describedby="password-hint"
        />
        {error && (
          <p role="alert" id="signup-error" className={styles.error}>
            {error}
          </p>
        )}
        <button type="submit" className={styles.primaryButton} disabled={loading}>
          {loading ? 'Creating…' : 'Sign up'}
        </button>
        <p className={styles.linksRow}>
          <a href={`/login?next=${encodeURIComponent(next)}`}>Have an account? Sign in</a>
          <span aria-hidden>·</span>
          <a href={`/magic/request?next=${encodeURIComponent(next)}`}>Use Magic Link</a>
        </p>
      </form>
    </main>
  );
}
