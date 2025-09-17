'use client';

import React from 'react';
import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { useSession } from 'next-auth/react';
// TenantSwitcher intentionally omitted from TopBar to free space; switching lives in ProfileMenu
import { NewAgentButton } from './NewAgentButton';
import { NavDrawer } from './NavDrawer';
import { ProfileMenu } from './ProfileMenu';
import { cn } from '../lib/cn';

function NavLink({ href, children }: { href: string; children: React.ReactNode }) {
  const pathname = usePathname() || '';
  const isActive = pathname === href || pathname.startsWith(href + '/');
  return (
    <Link
      href={href}
      aria-current={isActive ? 'page' : undefined}
      className={cn('px-2 py-1 rounded-md text-sm hover:opacity-100 focus-ring', {
        'opacity-100': isActive,
        'opacity-75': !isActive,
      })}
    >
      {children}
    </Link>
  );
}

export function TopBar() {
  // const pathname = usePathname() || '';
  const { data: session } = useSession();
  const isAuthed = Boolean(
    (session as unknown as { user?: { email?: string } } | null)?.user?.email,
  );
  const canCreate = Boolean((session as unknown as { canCreate?: boolean } | null)?.canCreate);
  // Derive admin per selected tenant membership rather than a flat isAdmin boolean to avoid leaking admin UI
  type Membership = { tenantId?: string; tenantSlug?: string; role?: string; roles?: string[] };
  const memberships: Membership[] =
    (session as unknown as { memberships?: Membership[] })?.memberships || [];
  const selectedTenant = (session as unknown as { tenant?: string })?.tenant;
  // Accept either role or roles[] containing 'admin'
  const isAdmin = memberships.some((m) => {
    if (!selectedTenant) return false;
    const matches = m.tenantSlug === selectedTenant || m.tenantId === selectedTenant;
    if (!matches) return false;
    const roles = [m.role, ...(Array.isArray(m.roles) ? m.roles : [])].filter(Boolean) as string[];
    return roles.some((r) => r?.toLowerCase() === 'admin');
  });
  // Tenant switcher moved into ProfileMenu; keep logic for potential future use

  // Centralized primary nav items for desktop. Agents is now a first-class entry.
  const navItems = [
    { label: 'Dashboard', href: '/' },
    { label: 'Agents', href: '/studio/agents' },
    // Keep existing quick-links; these may evolve with the new nav later.
    { label: 'Shepherd', href: '/shepherd/step1' },
    { label: 'Editor', href: '/editor' },
  ] as const;

  const [drawerOpen, setDrawerOpen] = React.useState(false);
  const [elevated, setElevated] = React.useState(false);

  // Add a subtle elevation when the page is scrolled
  React.useEffect(() => {
    const onScroll = () => {
      // Using window.scrollY is safe in client components
      setElevated(window.scrollY > 0);
    };
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
    return () => window.removeEventListener('scroll', onScroll);
  }, []);

  return (
    <header
      data-elevated={elevated ? 'true' : 'false'}
      className={cn(
        'sticky top-0 z-40 w-full border-b border-[var(--color-line)] bg-[var(--color-surface)]/80 backdrop-blur supports-[backdrop-filter]:bg-[var(--color-surface)]/60',
        elevated && 'shadow-sm',
      )}
    >
      <div className="mx-auto flex h-12 max-w-screen-2xl items-center gap-3 px-3">
        {/* Mobile hamburger */}
        <button
          type="button"
          aria-label="Open navigation"
          className="sm:hidden inline-flex h-8 w-8 items-center justify-center rounded-md border border-line bg-[var(--color-surface-raised)] focus-ring"
          onClick={() => setDrawerOpen(true)}
        >
          <span className="sr-only">Open navigation</span>
          <span aria-hidden className="block h-3 w-4">
            <span className="block h-0.5 w-full bg-ink"></span>
            <span className="mt-1 block h-0.5 w-full bg-ink"></span>
            <span className="mt-1 block h-0.5 w-full bg-ink"></span>
          </span>
        </button>
        <Link href="/" className="font-semibold tracking-tight mr-2">
          Appostolic
        </Link>
        {isAuthed && selectedTenant ? (
          <nav className="hidden sm:flex items-center gap-1" aria-label="Main navigation">
            {navItems.map((item) => (
              <NavLink key={item.href} href={item.href}>
                {item.label}
              </NavLink>
            ))}
            {isAdmin ? <AdminDropdown /> : null}
          </nav>
        ) : (
          <nav className="hidden sm:flex items-center gap-1" aria-label="Main navigation" />
        )}
        <div className="ml-auto flex items-center gap-2">
          {isAuthed && selectedTenant ? (
            <>
              {canCreate ? (
                <Link
                  href="/shepherd/step1"
                  className="px-3 py-1 rounded-md text-sm font-medium text-white bg-[var(--color-accent-600)] hover:brightness-110"
                >
                  Create Lesson
                </Link>
              ) : null}
              {/* Creator-only CTA to quickly add an Agent */}
              {canCreate ? <NewAgentButton /> : null}
              <ProfileMenu />
            </>
          ) : (
            <Link
              href="/login"
              className="px-3 py-1 rounded-md text-sm font-medium border border-line hover:bg-[var(--color-surface-raised)]"
            >
              Sign in
            </Link>
          )}
        </div>
        {/* Mobile Nav Drawer */}
        <NavDrawer
          open={drawerOpen}
          onClose={() => setDrawerOpen(false)}
          isAdmin={isAdmin}
          navItems={navItems as unknown as { label: string; href: string }[]}
          adminItems={[
            { label: 'Members', href: '/studio/admin/members' },
            { label: 'Invites', href: '/studio/admin/invites' },
            { label: 'Audits', href: '/studio/admin/audits' },
            { label: 'Notifications', href: '/studio/admin/notifications' },
          ]}
        />
      </div>
    </header>
  );
}

function AdminDropdown() {
  const router = useRouter();
  const [open, setOpen] = React.useState(false);
  const btnRef = React.useRef<HTMLButtonElement | null>(null);
  React.useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => {
      const target = e.target as Node | null;
      if (!btnRef.current) return;
      if (!btnRef.current.contains(target)) setOpen(false);
    };
    document.addEventListener('click', onDoc);
    return () => document.removeEventListener('click', onDoc);
  }, [open]);

  return (
    <div className="relative">
      <button
        ref={btnRef}
        type="button"
        className="px-2 py-1 rounded-md text-sm opacity-75 hover:opacity-100 focus-ring"
        aria-haspopup="menu"
        onClick={() => setOpen((v) => !v)}
      >
        Admin
      </button>
      {open ? (
        <div
          role="menu"
          className="absolute left-0 mt-2 w-44 rounded-md border border-line bg-[var(--color-surface)] p-1 shadow-lg"
        >
          <button
            role="menuitem"
            className="block w-full rounded px-2 py-1 text-left text-sm hover:bg-[var(--color-surface-raised)]"
            onClick={() => router.push('/studio/admin/members')}
          >
            Members
          </button>
          <button
            role="menuitem"
            className="block w-full rounded px-2 py-1 text-left text-sm hover:bg-[var(--color-surface-raised)]"
            onClick={() => router.push('/studio/admin/invites')}
          >
            Invites
          </button>
          <button
            role="menuitem"
            className="block w-full rounded px-2 py-1 text-left text-sm hover:bg-[var(--color-surface-raised)]"
            onClick={() => router.push('/studio/admin/audits')}
          >
            Audits
          </button>
          <button
            role="menuitem"
            className="block w-full rounded px-2 py-1 text-left text-sm hover:bg-[var(--color-surface-raised)]"
            onClick={() => router.push('/studio/admin/notifications')}
          >
            Notifications
          </button>
        </div>
      ) : null}
    </div>
  );
}
