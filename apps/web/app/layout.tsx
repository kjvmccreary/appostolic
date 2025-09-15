import './globals.css';
import React from 'react';
import Providers from './providers';
import { TenantSwitcher } from '../src/components/TenantSwitcher';
import { headers } from 'next/headers';
import { ThemeToggle } from '../src/components/ThemeToggle';

export default function RootLayout({ children }: { children: React.ReactNode }) {
  // Hide the global TenantSwitcher when we're on the /select-tenant page to avoid duplicate UI
  const path = (() => {
    try {
      return headers().get('x-pathname') || '';
    } catch {
      return '';
    }
  })();
  // Show TenantSwitcher only on protected sections; hide on all non-auth routes
  const protectedPrefixes = ['/studio', '/dev'];
  const showTenantSwitcher = protectedPrefixes.some((p) => path === p || path.startsWith(p + '/'));
  return (
    <html lang="en">
      <body>
        <Providers>
          {showTenantSwitcher && (
            <div className="flex items-center gap-3 p-2">
              <TenantSwitcher />
              <ThemeToggle />
              <a href="/change-password" className="text-sm opacity-80 hover:opacity-100">
                Change password
              </a>
            </div>
          )}
          {children}
        </Providers>
      </body>
    </html>
  );
}
