import './globals.css';
import React from 'react';
import Providers from './providers';
import { TenantSwitcher } from '../src/components/TenantSwitcher';
import { headers } from 'next/headers';

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
          {showTenantSwitcher && <TenantSwitcher />}
          {children}
        </Providers>
      </body>
    </html>
  );
}
