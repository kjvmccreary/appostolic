'use client';
import React, { useEffect, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { signIn } from 'next-auth/react';

export default function MagicVerifyClient() {
  const params = useSearchParams();
  const getParam = (key: string) => params?.get(key) ?? null;
  const router = useRouter();
  const token = getParam('token') ?? '';
  const next = getParam('next') ?? '/studio/agents';
  const postAuth = `/select-tenant?next=${encodeURIComponent(next)}`;

  const [status, setStatus] = useState<'idle' | 'verifying' | 'done' | 'error'>('idle');
  const [message, setMessage] = useState<string>('');

  useEffect(() => {
    async function run() {
      if (!token) {
        setStatus('error');
        setMessage('Missing token. Please use the link from your email.');
        return;
      }
      setStatus('verifying');
      try {
        const result = await signIn('credentials', {
          magicToken: token,
          redirect: false,
        });
        if (result?.error) {
          setStatus('error');
          setMessage(result.error || 'Unable to sign in with magic link.');
          return;
        }
        setStatus('done');
        setTimeout(() => router.replace(postAuth), 300);
      } catch {
        setStatus('error');
        setMessage('Unexpected error verifying token.');
      }
    }
    run();
  }, [token, next, router]);

  return (
    <main className="mx-auto max-w-md p-6">
      <h1 className="text-2xl font-semibold mb-4">Verifying magic link…</h1>
      {status === 'verifying' && <p>One moment…</p>}
      {status === 'done' && <p>Success! Redirecting…</p>}
      {status === 'error' && (
        <p role="alert" className="text-red-600 text-sm">
          {message}
        </p>
      )}
    </main>
  );
}
