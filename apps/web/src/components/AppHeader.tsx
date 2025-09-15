'use client';

import React from 'react';
import { usePathname } from 'next/navigation';
import { TenantSwitcher } from './TenantSwitcher';
import { ThemeToggle } from './ThemeToggle';

const protectedPrefixes = ['/studio', '/dev'];

export function AppHeader() {
  const pathname = usePathname() || '';
  const showTenantSwitcher = protectedPrefixes.some(
    (p) => pathname === p || pathname.startsWith(p + '/'),
  );
  return (
    <div className="flex items-center gap-3 p-2">
      {showTenantSwitcher && <TenantSwitcher />}
      <ThemeToggle />
      <a href="/change-password" className="text-sm opacity-80 hover:opacity-100">
        Change password
      </a>
    </div>
  );
}
