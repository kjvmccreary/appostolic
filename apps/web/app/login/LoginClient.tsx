'use client';
import React, { FormEvent, useState } from 'react';
import { signIn, useSession } from 'next-auth/react';
import { useSearchParams, useRouter } from 'next/navigation';

export default function LoginClient() {
  const params = useSearchParams();
  const getParam = React.useCallback((key: string) => params?.get(key) ?? null, [params]);
  const router = useRouter();
  const next = getParam('next') ?? '/studio/agents';
  // After sign-in, route through select-tenant and force tenant reselection for multi-tenant users.
  const postAuth = `/select-tenant?${new URLSearchParams({ next, reselect: '1' }).toString()}`;
  const loggedOut = getParam('loggedOut') === '1';
  const errorParam = getParam('error');
  const { data: session } = useSession();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

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
    <main className="mx-auto max-w-sm p-6">
      <h1 className="mb-2 text-2xl font-semibold">Sign in</h1>
      <p className="mb-6 text-sm text-muted">Enter your credentials to continue.</p>
      <form onSubmit={onSubmit} className="space-y-4" aria-describedby="login-help">
        <div>
          <label htmlFor="email" className="mb-1 block text-sm font-medium">
            Email
          </label>
          <input
            id="email"
            name="email"
            type="email"
            autoComplete="email"
            placeholder="you@example.com"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
            className="w-full rounded-md border border-line bg-[var(--color-surface)] px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-[var(--color-accent-600)]"
          />
        </div>
        <div>
          <label htmlFor="password" className="mb-1 block text-sm font-medium">
            Password
          </label>
          <input
            id="password"
            name="password"
            type="password"
            autoComplete="current-password"
            placeholder="••••••••"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            className="w-full rounded-md border border-line bg-[var(--color-surface)] px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-[var(--color-accent-600)]"
          />
          <p id="login-help" className="mt-1 text-xs text-muted">
            Use the magic link if you forgot your password.
          </p>
        </div>
        {error && (
          <p className="text-sm text-[crimson]" role="alert">
            {error}
          </p>
        )}
        <button
          type="submit"
          disabled={submitting}
          className="h-9 w-full rounded-md bg-[var(--color-accent-600)] px-3 text-sm font-medium text-white disabled:opacity-60"
        >
          {submitting ? 'Signing in…' : 'Sign in'}
        </button>
        <p className="text-sm">
          <a href="/forgot-password" className="text-[var(--color-accent-600)] hover:underline">
            Forgot password?
          </a>
          <span className="px-1" aria-hidden>
            ·
          </span>
          <a
            href={`/signup?next=${encodeURIComponent(next)}`}
            className="text-[var(--color-accent-600)] hover:underline"
          >
            Sign up
          </a>
          <span className="px-1" aria-hidden>
            ·
          </span>
          <a
            href={`/magic/request?next=${encodeURIComponent(next)}`}
            className="text-[var(--color-accent-600)] hover:underline"
          >
            Use Magic Link
          </a>
        </p>
      </form>
    </main>
  );
}
