'use client';

import React from 'react';
import { useSession, signOut } from 'next-auth/react';
import { TenantSwitcherModal } from './TenantSwitcherModal';
import { useColorScheme } from '../theme/ColorSchemeContext';
import { User, Sun, Moon, Monitor, Contrast } from 'lucide-react';

/**
 * ProfileMenu — small dropdown menu for account actions.
 * - Items: Profile (placeholder), Switch tenant (opens modal), Sign out
 * - Shows Superadmin chip when session indicates elevated role
 */
function useColorSchemeOptional() {
  try {
    return useColorScheme();
  } catch {
    return {
      mode: 'system',
      toggleMode: () => {},
      amoled: false,
      toggleAmoled: () => {},
    } as unknown as ReturnType<typeof useColorScheme>;
  }
}

export function ProfileMenu() {
  const { data: session } = useSession();
  const [open, setOpen] = React.useState(false);
  const [switcherOpen, setSwitcherOpen] = React.useState(false);
  const btnRef = React.useRef<HTMLButtonElement | null>(null);
  const { mode, toggleMode, amoled, toggleAmoled } = useColorSchemeOptional();

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
      {isSuper ? (
        <span className="mr-2 rounded bg-amber-600/20 px-1.5 py-0.5 text-xs text-amber-500">
          Superadmin
        </span>
      ) : null}
      <button
        ref={btnRef}
        type="button"
        className="inline-flex h-8 w-8 items-center justify-center rounded-md border border-line bg-[var(--color-surface-raised)] focus-ring"
        aria-label="Account"
        aria-haspopup="menu"
        aria-expanded={open ? 'true' : 'false'}
        onClick={onToggle}
        title="Account"
      >
        <User size={18} />
      </button>
      {open ? (
        <div
          role="menu"
          className="absolute right-0 mt-2 w-48 rounded-md border border-line bg-[var(--color-surface)] p-1 shadow-lg"
        >
          {isSuper ? <div className="px-2 py-1 text-xs text-amber-500">Superadmin</div> : null}
          <button
            role="menuitem"
            className="flex items-center gap-2 w-full rounded px-2 py-1 text-left text-sm hover:bg-[var(--color-surface-raised)]"
            onClick={toggleMode}
          >
            {mode === 'light' ? (
              <Sun size={16} />
            ) : mode === 'dark' ? (
              <Moon size={16} />
            ) : (
              <Monitor size={16} />
            )}
            <span>Theme: {mode}</span>
          </button>
          <button
            role="menuitem"
            className="flex items-center gap-2 w-full rounded px-2 py-1 text-left text-sm hover:bg-[var(--color-surface-raised)]"
            onClick={toggleAmoled}
          >
            <Contrast size={16} />
            <span>AMOLED: {amoled ? 'on' : 'off'}</span>
          </button>
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
