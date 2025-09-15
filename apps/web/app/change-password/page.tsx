'use client';
import * as React from 'react';

export default function ChangePasswordPage() {
  const [current, setCurrent] = React.useState('');
  const [pw, setPw] = React.useState('');
  const [status, setStatus] = React.useState<string | null>(null);
  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setStatus('Saving…');
    const res = await fetch('/api-proxy/auth/change-password', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ currentPassword: current, newPassword: pw }),
    });
    setStatus(res.ok ? 'Password changed.' : 'Change failed: check current password.');
  };
  return (
    <main className="page-wrap">
      <h1>Change Password</h1>
      <form onSubmit={onSubmit}>
        <label>
          Current password
          <input
            type="password"
            value={current}
            onChange={(e) => setCurrent(e.target.value)}
            required
          />
        </label>
        <label>
          New password
          <input type="password" value={pw} onChange={(e) => setPw(e.target.value)} required />
        </label>
        <button type="submit">Save</button>
      </form>
      {status && <p aria-live="polite">{status}</p>}
    </main>
  );
}
