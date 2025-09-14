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
  const showTenantSwitcher = !path.startsWith('/select-tenant');
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
