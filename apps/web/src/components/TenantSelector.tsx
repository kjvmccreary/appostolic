'use client';
import * as React from 'react';

export function TenantSelector() {
  const [tenant, setTenant] = React.useState<string>('');
  const [saving, setSaving] = React.useState(false);
  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!tenant.trim()) return;
    setSaving(true);
    try {
      const res = await fetch('/api/tenant/select', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tenant }),
      });
      if (!res.ok) {
        console.error('Failed to select tenant');
      }
    } finally {
      setSaving(false);
    }
  };
  return (
    <form onSubmit={onSubmit}>
      <input
        type="text"
        placeholder="tenant slug"
        value={tenant}
        onChange={(e) => setTenant(e.currentTarget.value)}
        aria-label="Tenant"
      />
      <button type="submit" disabled={saving || !tenant.trim()}>
        {saving ? 'Saving…' : 'Set Tenant'}
      </button>
    </form>
  );
}
