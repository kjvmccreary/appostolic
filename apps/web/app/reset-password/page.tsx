'use client';
import * as React from 'react';

export default function ResetPasswordPage() {
  const params = new URLSearchParams(typeof window !== 'undefined' ? window.location.search : '');
  const [token, setToken] = React.useState(params.get('token') || '');
  const [pw, setPw] = React.useState('');
  const [status, setStatus] = React.useState<string | null>(null);
  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setStatus('Resetting…');
    const res = await fetch('/api-proxy/auth/reset-password', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ token, newPassword: pw }),
    });
    setStatus(
      res.ok ? 'Password reset. You can now log in.' : 'Reset failed: invalid or expired token.',
    );
  };
  return (
    <main className="page-wrap">
      <h1>Reset Password</h1>
      <form onSubmit={onSubmit}>
        <label>
          Token
          <input value={token} onChange={(e) => setToken(e.target.value)} required />
        </label>
        <label>
          New password
          <input type="password" value={pw} onChange={(e) => setPw(e.target.value)} required />
        </label>
        <button type="submit">Reset password</button>
      </form>
      {status && <p aria-live="polite">{status}</p>}
    </main>
  );
}
