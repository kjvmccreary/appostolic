'use client';

import React from 'react';
import { createPortal } from 'react-dom';
import { getFlagRoles, type FlagRole } from '../lib/roles';
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
  const LAST_KEY = 'last_selected_tenant';

  const memberships =
    (
      session as unknown as {
        memberships?: { tenantSlug: string; role?: string; roles?: FlagRole[] }[];
      }
    )?.memberships ?? [];
  const current = (session as unknown as { tenant?: string })?.tenant ?? '';
  const [lastSelected, setLastSelected] = React.useState<string | null>(null);

  React.useEffect(() => {
    if (!open) return;
    try {
      const v = window.localStorage.getItem(LAST_KEY);
      if (v) setLastSelected(v);
    } catch {
      // ignore storage errors
    }
  }, [open]);

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
    try {
      window.localStorage.setItem(LAST_KEY, slug);
    } catch {
      // ignore
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

  const labelFor = (m: { role?: string; roles?: FlagRole[] }): string => {
    // Prefer flags if present, otherwise derive from legacy role via getFlagRoles
    const flags = getFlagRoles({
      tenantId: '' as unknown as string,
      tenantSlug: '' as unknown as string,
      role: (m.role as unknown as 'Owner' | 'Admin' | 'Editor' | 'Viewer') || 'Viewer',
      roles: m.roles,
    } as unknown as {
      tenantId: string;
      tenantSlug: string;
      role: 'Owner' | 'Admin' | 'Editor' | 'Viewer';
      roles?: FlagRole[];
    });
    const precedence: FlagRole[] = ['TenantAdmin', 'Approver', 'Creator', 'Learner'];
    for (const r of precedence) {
      if (flags.includes(r)) return r === 'TenantAdmin' ? 'Admin' : r;
    }
    return 'Learner';
  };

  const modal = (
    <div className="fixed inset-0 z-[100]">
      <div
        className="absolute inset-0 bg-black/40"
        data-testid="tenant-switcher-backdrop"
        onClick={onBackdrop}
      />
      <div className="relative h-full w-full p-4 flex items-center justify-center overflow-y-auto">
        <div
          role="dialog"
          aria-modal="true"
          ref={panelRef}
          tabIndex={-1}
          className="relative w-[min(90vw,28rem)] max-h-[calc(100vh-4rem)] overflow-auto rounded-md border border-[var(--color-line)] bg-[var(--color-surface)] p-4 shadow-xl focus:outline-none"
        >
          <h2 className="mb-2 text-base font-semibold">Switch tenant</h2>
          <ul>
            {memberships.map((m) => {
              const active = m.tenantSlug === current;
              const hinted = !active && lastSelected && lastSelected === m.tenantSlug;
              return (
                <li key={m.tenantSlug}>
                  <button
                    type="button"
                    className={
                      'flex w-full items-center justify-between rounded px-3 py-2 text-left text-sm hover:bg-[var(--color-surface-raised)] focus-ring ' +
                      (hinted ? 'border border-dashed border-[var(--color-line)]' : '')
                    }
                    aria-current={active ? 'true' : undefined}
                    onClick={() => onSelect(m.tenantSlug)}
                  >
                    <span>{m.tenantSlug}</span>
                    <span
                      data-testid="role-badge"
                      data-role={active ? 'Current' : labelFor(m)}
                      className={
                        'ml-3 inline-flex items-center rounded-full px-2 py-0.5 text-[11px] ' +
                        (active
                          ? 'bg-emerald-500/15 text-emerald-500 border border-emerald-500/30'
                          : 'bg-[var(--color-surface-raised)] text-muted border border-line')
                      }
                    >
                      {active ? 'Current' : labelFor(m)}
                    </span>
                  </button>
                </li>
              );
            })}
          </ul>
        </div>
      </div>
    </div>
  );

  // Render via portal to avoid being constrained by header stacking/positioning
  if (typeof document !== 'undefined' && document.body) {
    return createPortal(modal, document.body);
  }
  return modal;
}
