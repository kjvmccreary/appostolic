import './globals.css';
import React from 'react';
import Providers from './providers';
import { TenantSwitcher } from '../src/components/TenantSwitcher';

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>
        <Providers>
          <TenantSwitcher />
          {children}
        </Providers>
      </body>
    </html>
  );
}
