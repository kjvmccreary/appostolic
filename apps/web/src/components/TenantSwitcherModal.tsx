'use client';

import React from 'react';
import { useSession } from 'next-auth/react';
import { useRouter } from 'next/navigation';

/**
 * TenantSwitcherModal
 * - Dialog with list of memberships from session
 * - Selecting a tenant updates the session (JWT), POSTs to /api/tenant/select, and refreshes
 * - Focus trap while open; restore on close; ESC and backdrop close
 */
export function TenantSwitcherModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { data: session, update } = useSession();
  const router = useRouter();
  const panelRef = React.useRef<HTMLDivElement | null>(null);
  const lastActive = React.useRef<HTMLElement | null>(null);

  const memberships =
    (session as unknown as { memberships?: { tenantSlug: string; role?: string }[] })
      ?.memberships ?? [];
  const current = (session as unknown as { tenant?: string })?.tenant ?? '';

  React.useEffect(() => {
    if (open) {
      lastActive.current = (document.activeElement as HTMLElement) ?? null;
      panelRef.current?.focus();
      const onKey = (e: KeyboardEvent) => {
        if (e.key === 'Escape') {
          e.preventDefault();
          onClose();
        }
      };
      document.addEventListener('keydown', onKey);
      return () => document.removeEventListener('keydown', onKey);
    } else {
      lastActive.current?.focus?.();
    }
  }, [open, onClose]);

  const onBackdrop = (e: React.MouseEvent) => {
    e.stopPropagation();
    onClose();
  };

  const onSelect = async (slug: string) => {
    if (!slug || slug === current) {
      onClose();
      return;
    }
    await update({ tenant: slug });
    await fetch('/api/tenant/select', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ tenant: slug }),
    });
    router.refresh();
    onClose();
  };

  if (!open) return null;

  return (
    <div role="dialog" aria-modal="true" className="fixed inset-0 z-50">
      <div className="absolute inset-0 bg-black/40" onClick={onBackdrop} />
      <div
        ref={panelRef}
        tabIndex={-1}
        className="absolute left-1/2 top-1/2 w-[min(90vw,28rem)] -translate-x-1/2 -translate-y-1/2 rounded-md border border-[var(--color-line)] bg-[var(--color-surface)] p-4 shadow-xl focus:outline-none"
      >
        <h2 className="mb-2 text-base font-semibold">Switch tenant</h2>
        <ul className="max-h-72 overflow-auto">
          {memberships.map((m) => {
            const active = m.tenantSlug === current;
            return (
              <li key={m.tenantSlug}>
                <button
                  type="button"
                  className="flex w-full items-center justify-between rounded px-3 py-2 text-left text-sm hover:bg-[var(--color-surface-raised)] focus-ring"
                  aria-current={active ? 'true' : undefined}
                  onClick={() => onSelect(m.tenantSlug)}
                >
                  <span>{m.tenantSlug}</span>
                  <span className="text-xs text-muted">{active ? 'Current' : m.role}</span>
                </button>
              </li>
            );
          })}
        </ul>
      </div>
    </div>
  );
}
