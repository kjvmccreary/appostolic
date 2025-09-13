import './globals.css';
import React from 'react';
import Providers from './providers';
import { TenantSelector } from '../src/components/TenantSelector';

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>
        <Providers>
          <TenantSelector />
          {children}
        </Providers>
      </body>
    </html>
  );
}
