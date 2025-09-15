import './globals.css';
import React from 'react';
import Providers from './providers';
import { AppHeader } from '../src/components/AppHeader';

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>
        <Providers>
          <AppHeader />
          {children}
        </Providers>
      </body>
    </html>
  );
}
