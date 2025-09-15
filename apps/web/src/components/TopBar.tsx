'use client';

import React from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useSession } from 'next-auth/react';
import { TenantSwitcher } from './TenantSwitcher';
import { ThemeToggle } from './ThemeToggle';
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
  const pathname = usePathname() || '';
  const { data: session } = useSession();
  const canCreate = Boolean((session as unknown as { canCreate?: boolean } | null)?.canCreate);
  const isAdmin = Boolean((session as unknown as { isAdmin?: boolean } | null)?.isAdmin);
  // Show TenantSwitcher broadly (dashboard and most app pages) and hide only on specific public/auth pages
  const hideTenantOn = ['/select-tenant', '/login', '/signup'];
  const hideTenant = hideTenantOn.some((p) => pathname === p || pathname.startsWith(p + '/'));

  // Centralized primary nav items for desktop. Agents is now a first-class entry.
  const navItems = [
    { label: 'Dashboard', href: '/' },
    { label: 'Agents', href: '/studio/agents' },
    // Keep existing quick-links; these may evolve with the new nav later.
    { label: 'Shepherd', href: '/shepherd/step1' },
    { label: 'Editor', href: '/editor' },
  ] as const;

  const [drawerOpen, setDrawerOpen] = React.useState(false);

  return (
    <header className="sticky top-0 z-40 w-full border-b border-[var(--color-line)] bg-[var(--color-surface)]/80 backdrop-blur supports-[backdrop-filter]:bg-[var(--color-surface)]/60">
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
        {!hideTenant ? <TenantSwitcher /> : null}
        <Link href="/" className="font-semibold tracking-tight mr-2">
          Appostolic
        </Link>
        <nav className="hidden sm:flex items-center gap-1" aria-label="Main navigation">
          {navItems.map((item) => (
            <NavLink key={item.href} href={item.href}>
              {item.label}
            </NavLink>
          ))}
        </nav>
        <div className="ml-auto flex items-center gap-2">
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
          <ThemeToggle />
          <ProfileMenu />
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
