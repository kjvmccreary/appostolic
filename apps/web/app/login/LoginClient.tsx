'use client';
import React, { FormEvent, useRef, useState } from 'react';
import { signIn, useSession } from 'next-auth/react';
import { useSearchParams, useRouter } from 'next/navigation';
import styles from './styles.module.css';

export default function LoginClient() {
  const params = useSearchParams();
  const router = useRouter();
  const next = params.get('next') || '/studio/agents';
  // After sign-in, route through select-tenant to enforce selection when user has multiple memberships
  const postAuth = `/select-tenant?next=${encodeURIComponent(next)}`;
  const loggedOut = params.get('loggedOut') === '1';
  const errorParam = params.get('error');
  const { data: session } = useSession();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [csrfToken, setCsrfToken] = useState<string>('');
  const [submitting, setSubmitting] = useState(false);

  // Load CSRF token (Auth.js anti-CSRF) once per mount
  const csrfRequestedRef = useRef(false);
  React.useEffect(() => {
    if (csrfRequestedRef.current || typeof window === 'undefined') return;
    csrfRequestedRef.current = true;
    let alive = true;
    (async () => {
      try {
        const r = await fetch('/api/auth/csrf');
        const d = await r.json();
        if (alive) setCsrfToken(d.csrfToken ?? '');
      } catch {
        // ignore
      }
    })();
    return () => {
      alive = false;
    };
  }, []);

  // Redirect to next when already authenticated (but not immediately after logout)
  React.useEffect(() => {
    if (session?.user?.email && !loggedOut) {
      router.replace(postAuth);
    }
  }, [session?.user?.email, loggedOut, postAuth, router]);

  // Surface auth provider error (e.g., CredentialsSignin) from query param
  React.useEffect(() => {
    if (!errorParam) return;
    // Map common next-auth errors to friendly copy
    const map: Record<string, string> = {
      CredentialsSignin: 'Invalid email or password',
      AccessDenied: 'Access denied',
      default: 'Sign-in failed',
    };
    setError(map[errorParam] ?? map.default);
  }, [errorParam]);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    if (submitting) return;
    setError(null);
    setSubmitting(true);
    // Let NextAuth handle redirect after setting cookies to avoid race conditions
    // If credentials are invalid, NextAuth will redirect back with ?error=CredentialsSignin
    const testMode = process.env.NODE_ENV === 'test';
    const result = await signIn('credentials', {
      email,
      password,
      // In tests, disable automatic redirect so we can assert navigation
      redirect: !testMode,
      callbackUrl: postAuth,
    });
    // In test/mocked scenarios signIn may return an object instead of redirecting
    if (result && (result as unknown as { error?: string }).error) {
      setError('Invalid email or password');
      setSubmitting(false);
      return;
    }
    if (testMode && result && !(result as unknown as { error?: string }).error) {
      router.replace(postAuth);
      return;
    }
    // No further code runs here on success due to redirect
  }

  return (
    <main className={styles.container}>
      <h1 className={styles.title}>Sign in</h1>
      <form onSubmit={onSubmit} className={styles.form}>
        {/* CSRF token hidden field for Auth.js */}
        <input type="hidden" name="csrfToken" value={csrfToken} />
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
        {error && <p className={styles.error}>{error}</p>}
        <button type="submit" className={styles.primaryButton} disabled={submitting}>
          Sign in
        </button>
        <p className={styles.linksRow}>
          <a href="/forgot-password">Forgot password?</a>
          <span aria-hidden>·</span>
          <a href={`/signup?next=${encodeURIComponent(next)}`}>Sign up</a>
          <span aria-hidden>·</span>
          <a href={`/magic/request?next=${encodeURIComponent(next)}`}>Use Magic Link</a>
        </p>
      </form>
    </main>
  );
}
