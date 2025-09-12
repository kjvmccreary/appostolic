'use client';
import { signIn, useSession } from 'next-auth/react';
import { useSearchParams, useRouter } from 'next/navigation';
import { FormEvent, useState } from 'react';
import styles from './styles.module.css';

export default function LoginPage() {
  const params = useSearchParams();
  const router = useRouter();
  const next = params.get('next') || '/studio/agents';
  const { data: session } = useSession();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [csrfToken, setCsrfToken] = useState<string>('');

  // Load CSRF token for the form (Auth.js anti-CSRF)
  // NextAuth exposes it at /api/auth/csrf; safe to fetch client-side
  // and include as a hidden input on the form.
  if (typeof window !== 'undefined' && !csrfToken) {
    // Fire-and-forget; avoids double-fetch due to React StrictMode by guarding on value
    fetch('/api/auth/csrf')
      .then((r) => r.json())
      .then((d) => setCsrfToken(d.csrfToken))
      .catch(() => {});
  }

  if (session?.user?.email) {
    router.replace(next);
    return null;
  }

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
      </form>
    </main>
  );
}
