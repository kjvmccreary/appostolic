'use client';
import * as React from 'react';
import { useSession } from 'next-auth/react';
import { useRouter } from 'next/navigation';

type Membership = { tenantId: string; tenantSlug: string; role: string };

export function TenantSwitcher() {
  const { data: session, update } = useSession();
  const router = useRouter();
  const memberships: Membership[] =
    (session as unknown as { memberships?: Membership[] })?.memberships ?? [];
  const current = (session as unknown as { tenant?: string })?.tenant ?? '';
  const [value, setValue] = React.useState<string>(current);
  const [saving, setSaving] = React.useState(false);

  React.useEffect(() => {
    // Keep local state in sync if session changes elsewhere
    setValue(current);
  }, [current]);

  const onChange = async (e: React.ChangeEvent<HTMLSelectElement>) => {
    const slug = e.currentTarget.value;
    setValue(slug);
    if (!slug) return;
    setSaving(true);
    try {
      // Persist selection in both JWT (session.update) and secure cookie for server-only reads
      await Promise.all([
        update({ tenant: slug }),
        fetch('/api/tenant/select', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ tenant: slug }),
        }),
      ]);
      router.refresh();
    } finally {
      setSaving(false);
    }
  };

  if (!memberships?.length) return null;

  return (
    <div aria-label="Tenant switcher" className="tenant-switcher">
      <label htmlFor="tenant-switcher-select">Tenant:</label>
      <select id="tenant-switcher-select" value={value} onChange={onChange} disabled={saving}>
        <option value="">Select…</option>
        {memberships.map((m) => (
          <option key={m.tenantId} value={m.tenantSlug}>
            {m.tenantSlug} — {m.role}
          </option>
        ))}
      </select>
    </div>
  );
}
