'use client';

import React from 'react';
import { useSession } from 'next-auth/react';
import { TenantSwitcherModal } from './TenantSwitcherModal';
import { useColorScheme } from '../theme/ColorSchemeContext';
import { User, Sun, Moon, Monitor, Contrast } from 'lucide-react';
import Link from 'next/link';

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
  const [avatarUrl, setAvatarUrl] = React.useState<string | null>(null);

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

  React.useEffect(() => {
    // Attempt to read initial avatar url from session (if present in profile)
    const anySession = session as unknown as { profile?: { avatar?: { url?: string } } } | null;
    const initial = anySession?.profile?.avatar?.url ?? null;
    if (initial) setAvatarUrl(initial);
  }, [session]);

  React.useEffect(() => {
    // Fallback hydration: on fresh login the NextAuth session may not include profile.avatar yet.
    // In that case, fetch /api-proxy/users/me once and pull avatar URL from the profile blob.
    // Guard: only run when authenticated (user.email present) and avatarUrl is still null.
    const anySession = session as unknown as { user?: { email?: string } } | null;
    const hasEmail = Boolean(anySession?.user?.email);
    let cancelled = false;
    async function hydrateFromProfile() {
      try {
        const res = await fetch('/api-proxy/users/me', {
          cache: 'no-store',
          credentials: 'include',
        });
        if (!res.ok) {
          if (res.status === 401) {
            try {
              const payload = (await res.json()) as { code?: string };
              console.warn('[ProfileMenu] hydrateFromProfile unauthorized', payload);
            } catch {
              console.warn('[ProfileMenu] hydrateFromProfile unauthorized (no payload)');
            }
          }
          return;
        }
        const json = (await res.json()) as { profile?: { avatar?: { url?: string } } };
        const url = json?.profile?.avatar?.url ?? null;
        if (!cancelled && url) setAvatarUrl(url);
      } catch {
        // non-fatal; leave as default icon
      }
    }
    if (hasEmail && !avatarUrl) {
      void hydrateFromProfile();
    }
    return () => {
      cancelled = true;
    };
  }, [session, avatarUrl]);

  React.useEffect(() => {
    const handler = (e: Event) => {
      const detail = (e as CustomEvent).detail as { url?: string } | undefined;
      if (detail?.url) setAvatarUrl(detail.url);
    };
    window.addEventListener('avatar-updated', handler as EventListener);
    return () => window.removeEventListener('avatar-updated', handler as EventListener);
  }, []);

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
        className="inline-flex h-8 w-8 items-center justify-center rounded-md border border-line bg-[var(--color-surface-raised)] focus-ring overflow-hidden"
        aria-label="Account"
        aria-haspopup="menu"
        {...{ 'aria-expanded': open ? 'true' : 'false' }}
        onClick={onToggle}
        title="Account"
      >
        {avatarUrl ? (
          <img src={avatarUrl} alt="Avatar" className="h-full w-full object-cover" />
        ) : (
          <User size={18} />
        )}
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
          <Link
            href="/profile"
            role="menuitem"
            className="block w-full rounded px-2 py-1 text-left text-sm hover:bg-[var(--color-surface-raised)]"
            onClick={onClose}
          >
            Profile
          </Link>
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
            onClick={() => {
              // Route through /logout so both client and middleware can clear the selected_tenant cookie
              // and ensure multi-tenant users must explicitly re-select a tenant after next login.
              window.location.href = '/logout';
            }}
          >
            Sign out
          </button>
        </div>
      ) : null}
      <TenantSwitcherModal open={switcherOpen} onClose={() => setSwitcherOpen(false)} />
    </div>
  );
}
