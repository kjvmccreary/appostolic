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
import { computeBooleansForTenant } from '../lib/roles';

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
  const DEBUG = (process.env.NEXT_PUBLIC_DEBUG_ADMIN_GATING ?? 'false').toLowerCase() === 'true';
  const isAuthed = Boolean(
    (session as unknown as { user?: { email?: string } } | null)?.user?.email,
  );
  const canCreate = Boolean((session as unknown as { canCreate?: boolean } | null)?.canCreate);
  const selectedTenant = (session as unknown as { tenant?: string })?.tenant;
  // IMPORTANT: Do NOT trust a global session.isAdmin. Admin visibility must be
  // tenant-scoped. We compute admin strictly from the membership that matches
  // the currently selected tenant to avoid leakage from stale or global flags.
  type RolesMembership = { tenantId: string; tenantSlug: string; role: string; roles?: string[] };
  const rawMemberships = (session as unknown as { memberships?: RolesMembership[] })?.memberships;
  const memberships = Array.isArray(rawMemberships) ? rawMemberships : [];
  // Support session.tenant being either a slug or an id by resolving to slug if needed.
  const effectiveSlug = React.useMemo(() => {
    if (!selectedTenant) return null;
    // If it already matches a slug, keep as-is; otherwise, try to find by id and use its slug.
    const bySlug = memberships.find((m) => m.tenantSlug === selectedTenant);
    if (bySlug) return bySlug.tenantSlug;
    const byId = memberships.find((m) => m.tenantId === selectedTenant);
    return byId ? byId.tenantSlug : selectedTenant; // fall back to provided value
  }, [selectedTenant, memberships]);

  const { isAdmin, roles: effectiveRoles } = computeBooleansForTenant(
    memberships as unknown as Parameters<typeof computeBooleansForTenant>[0],
    effectiveSlug,
  );
  const isAdminGated = isAdmin; // flags are authoritative now
  if (DEBUG && isAuthed && selectedTenant) {
    console.groupCollapsed(
      `%c[AdminGate] tenant=%s email=%s isAdmin=%s roles=%o`,
      'color:#888',
      String(effectiveSlug ?? selectedTenant),
      (session as unknown as { user?: { email?: string } } | null)?.user?.email ?? '(unknown)',
      String(isAdminGated),
      effectiveRoles,
    );
    console.groupEnd();
  }
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
        {/* Brand with tenant label just below to aid cross-tenant debugging */}
        <div className="mr-2 leading-none">
          <Link href="/" className="font-semibold tracking-tight block">
            Appostolic
          </Link>
          {/* Show selected tenant slug (or id) below the brand when authenticated */}
          {isAuthed && selectedTenant ? (
            <div
              data-testid="tenant-label"
              className="mt-0.5 text-[11px] text-[color:var(--color-muted)]"
              aria-label="Selected tenant"
            >
              {String(selectedTenant)}
            </div>
          ) : null}
        </div>
        {isAuthed && selectedTenant ? (
          <nav className="hidden sm:flex items-center gap-1" aria-label="Main navigation">
            {navItems.map((item) => (
              <NavLink key={item.href} href={item.href}>
                {item.label}
              </NavLink>
            ))}
            {isAdminGated ? <AdminDropdown /> : null}
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
              {/* Display current user's email immediately to the left of the avatar to help debug cross-tenant state */}
              {isAuthed ? (
                <span
                  data-testid="user-email"
                  className="hidden sm:inline text-xs text-[color:var(--color-muted)] mr-1"
                  aria-label="Current user email"
                >
                  {(session as unknown as { user?: { email?: string } } | null)?.user?.email ?? ''}
                </span>
              ) : null}
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
          isAdmin={isAdminGated}
          navItems={navItems as unknown as { label: string; href: string }[]}
          adminItems={[
            { label: 'Org Settings', href: '/studio/admin/settings' },
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
            onClick={() => router.push('/studio/admin/settings')}
          >
            Org Settings
          </button>
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
