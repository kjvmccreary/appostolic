'use client';

import React from 'react';
import { useSession, signOut } from 'next-auth/react';
import { TenantSwitcherModal } from './TenantSwitcherModal';

/**
 * ProfileMenu — small dropdown menu for account actions.
 * - Items: Profile (placeholder), Switch tenant (opens modal), Sign out
 * - Shows Superadmin chip when session indicates elevated role
 */
export function ProfileMenu() {
  const { data: session } = useSession();
  const [open, setOpen] = React.useState(false);
  const [switcherOpen, setSwitcherOpen] = React.useState(false);
  const btnRef = React.useRef<HTMLButtonElement | null>(null);

  const isSuper = Boolean((session as unknown as { isSuperAdmin?: boolean } | null)?.isSuperAdmin);

  const onToggle = () => setOpen((v) => !v);
  const onClose = () => setOpen(false);

  React.useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => {
      const target = e.target as Node | null;
      if (!btnRef.current) return;
      if (!btnRef.current.contains(target)) {
        onClose();
      }
    };
    document.addEventListener('click', onDoc);
    return () => document.removeEventListener('click', onDoc);
  }, [open]);

  return (
    <div className="relative">
      <button
        ref={btnRef}
        type="button"
        className="rounded-md border border-line bg-[var(--color-surface-raised)] px-2 py-1 text-sm focus-ring"
        aria-haspopup="menu"
        aria-expanded={open ? 'true' : 'false'}
        onClick={onToggle}
      >
        {isSuper ? (
          <span className="mr-2 rounded bg-amber-600/20 px-1.5 py-0.5 text-xs text-amber-500">
            Superadmin
          </span>
        ) : null}
        <span>Account</span>
      </button>
      {open ? (
        <div
          role="menu"
          className="absolute right-0 mt-2 w-48 rounded-md border border-line bg-[var(--color-surface)] p-1 shadow-lg"
        >
          <button
            role="menuitem"
            className="block w-full rounded px-2 py-1 text-left text-sm hover:bg-[var(--color-surface-raised)]"
            onClick={() => alert('Profile coming soon')}
          >
            Profile
          </button>
          <button
            role="menuitem"
            className="block w-full rounded px-2 py-1 text-left text-sm hover:bg-[var(--color-surface-raised)]"
            onClick={() => {
              setSwitcherOpen(true);
              onClose();
            }}
          >
            Switch tenant…
          </button>
          <button
            role="menuitem"
            className="block w-full rounded px-2 py-1 text-left text-sm hover:bg-[var(--color-surface-raised)]"
            onClick={() => signOut({ callbackUrl: '/login' })}
          >
            Sign out
          </button>
        </div>
      ) : null}
      <TenantSwitcherModal open={switcherOpen} onClose={() => setSwitcherOpen(false)} />
    </div>
  );
}
