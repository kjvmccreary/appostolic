'use client';

import React from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { TenantSwitcher } from './TenantSwitcher';
import { ThemeToggle } from './ThemeToggle';
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
  // Show TenantSwitcher broadly (dashboard and most app pages) and hide only on specific public/auth pages
  const hideTenantOn = ['/select-tenant', '/login', '/signup'];
  const hideTenant = hideTenantOn.some((p) => pathname === p || pathname.startsWith(p + '/'));

  return (
    <header className="sticky top-0 z-40 w-full border-b border-[var(--color-line)] bg-[var(--color-surface)]/80 backdrop-blur supports-[backdrop-filter]:bg-[var(--color-surface)]/60">
      <div className="mx-auto flex h-12 max-w-screen-2xl items-center gap-3 px-3">
  {!hideTenant ? <TenantSwitcher /> : null}
        <Link href="/" className="font-semibold tracking-tight mr-2">
          Appostolic
        </Link>
        <nav className="hidden sm:flex items-center gap-1" aria-label="Main navigation">
          <NavLink href="/">Dashboard</NavLink>
          <NavLink href="/shepherd/step1">Shepherd</NavLink>
          <NavLink href="/editor">Editor</NavLink>
        </nav>
        <div className="ml-auto flex items-center gap-2">
          <Link
            href="/shepherd/step1"
            className="px-3 py-1 rounded-md text-sm font-medium text-white bg-[var(--color-accent-600)] hover:brightness-110"
          >
            Create Lesson
          </Link>
          <ThemeToggle />
        </div>
      </div>
    </header>
  );
}
