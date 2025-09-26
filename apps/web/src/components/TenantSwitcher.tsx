'use client';
import * as React from 'react';
import { useSession } from 'next-auth/react';
import { useRouter } from 'next/navigation';
import { ChevronDown } from 'lucide-react';
import { getFlagRoles, type FlagRole } from '../lib/roles';

type Membership = { tenantId: string; tenantSlug: string; roles?: FlagRole[] };

export function TenantSwitcher() {
  const { data: session, update } = useSession();
  const router = useRouter();
  const memberships: Membership[] =
    (session as unknown as { memberships?: Membership[] })?.memberships ?? [];
  const current = (session as unknown as { tenant?: string })?.tenant ?? '';
  const [value, setValue] = React.useState<string>(current);
  const [saving, setSaving] = React.useState(false);
  const [msg, setMsg] = React.useState<string | null>(null);

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
      // 1) Update JWT via next-auth; returns the updated session
      await update({ tenant: slug });
      // 2) Provide feedback (console + transient on-screen message)
      console.info('[TenantSwitcher] Tenant updated in JWT', { tenant: slug });
      setMsg(`Switched to tenant "${slug}"`);
      // Auto-clear message after a short delay
      setTimeout(() => setMsg(null), 2500);

      // 3) Persist secure cookie for server-only reads
      await fetch('/api/tenant/select', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tenant: slug }),
      });
      router.refresh();
    } finally {
      setSaving(false);
    }
  };

  if (!memberships?.length) return null;

  // Compute a canonical role label from roles[] flags only.
  const labelFor = (m: Membership): string => {
    const flags = getFlagRoles({
      tenantId: m.tenantId ?? '',
      tenantSlug: m.tenantSlug ?? '',
      roles: m.roles,
    });
    if (flags.includes('TenantAdmin')) return 'Admin';
    if (flags.includes('Approver')) return 'Approver';
    if (flags.includes('Creator')) return 'Creator';
    if (flags.includes('Learner')) return 'Learner';
    return 'Learner';
  };

  return (
    <div aria-label="Tenant switcher" className="tenant-switcher inline-flex items-center gap-2">
      <label htmlFor="tenant-switcher-select" className="text-xs text-muted">
        Tenant
      </label>
      <div className="relative">
        <select
          id="tenant-switcher-select"
          value={value}
          onChange={onChange}
          disabled={saving}
          className="h-8 min-w-[10rem] rounded-md border border-line bg-[var(--color-surface-raised)] pl-2 pr-7 text-sm text-ink focus-ring disabled:opacity-60 appearance-none"
        >
          <option value="">Select…</option>
          {memberships.map((m) => {
            const roleLabel = labelFor(m);
            return (
              <option key={m.tenantId} value={m.tenantSlug}>
                {m.tenantSlug} — {roleLabel}
              </option>
            );
          })}
        </select>
        <ChevronDown
          size={14}
          className="pointer-events-none absolute right-2 top-1/2 -translate-y-1/2 text-muted"
          aria-hidden
        />
      </div>
      {msg ? (
        <span role="status" aria-live="polite" className="text-xs text-muted">
          {msg}
        </span>
      ) : null}
    </div>
  );
}
