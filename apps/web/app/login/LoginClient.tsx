'use client';
import React, { FormEvent, useState } from 'react';
import { signIn, useSession } from 'next-auth/react';
import { useSearchParams, useRouter } from 'next/navigation';
import styles from './styles.module.css';

export default function LoginClient() {
  const params = useSearchParams();
  const router = useRouter();
  const next = params.get('next') || '/studio/agents';
  const loggedOut = params.get('loggedOut') === '1';
  const { data: session } = useSession();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [csrfToken, setCsrfToken] = useState<string>('');

  // Load CSRF token (Auth.js anti-CSRF) after mount
  // Guard against setting state on an unmounted component
  React.useEffect(() => {
    let alive = true;
    async function loadCsrf() {
      try {
        const r = await fetch('/api/auth/csrf');
        const d = await r.json();
        if (alive) setCsrfToken(d.csrfToken ?? '');
      } catch {
        // ignore
      }
    }
    if (typeof window !== 'undefined' && !csrfToken) {
      loadCsrf();
    }
    return () => {
      alive = false;
    };
  }, [csrfToken]);

  // Redirect to next when already authenticated (but not immediately after logout)
  React.useEffect(() => {
    if (session?.user?.email && !loggedOut) {
      router.replace(next);
    }
  }, [session?.user?.email, loggedOut, next, router]);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    const res = await signIn('credentials', {
      email,
      password,
      redirect: false,
      callbackUrl: next,
    });
    if (res?.error) {
      setError('Invalid email or password');
      return;
    }
    router.replace(next);
  }

  return (
    <main className={styles.container}>
      <h1>Sign in</h1>
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
        <button type="submit">Sign in</button>
        <p className={styles.forgotLink}>
          <a href="/forgot-password">Forgot password?</a>
        </p>
      </form>
    </main>
  );
}
