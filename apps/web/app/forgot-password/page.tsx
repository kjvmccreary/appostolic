'use client';
import * as React from 'react';

export default function ForgotPasswordPage() {
  const [email, setEmail] = React.useState('');
  const [status, setStatus] = React.useState<string | null>(null);
  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setStatus('Sending…');
    const res = await fetch('/api-proxy/auth/forgot-password', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ email }),
    });
    setStatus(res.ok ? 'If the email exists, a reset link was sent.' : 'Request accepted.');
  };
  return (
    <main className="page-wrap">
      <h1>Forgot Password</h1>
      <form onSubmit={onSubmit}>
        <label>
          Email
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
        </label>
        <button type="submit">Send reset link</button>
      </form>
      {status && <p aria-live="polite">{status}</p>}
    </main>
  );
}
