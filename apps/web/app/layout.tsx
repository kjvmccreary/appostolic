import './globals.css';
import React from 'react';
import Providers from './providers';
import { AppHeader } from '../src/components/AppHeader';

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <head>
        {/* Prevent theme flash on first paint: initialize classes before hydration */}
        <script
          dangerouslySetInnerHTML={{
            __html: `(() => { try {
  const d = document.documentElement;
  const storedMode = localStorage.getItem('theme') || 'system';
  const amoled = localStorage.getItem('amoled') === 'true';
  const prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
  const isDark = storedMode === 'dark' || (storedMode === 'system' && prefersDark);
  d.classList.toggle('dark', !!isDark);
  if (isDark && amoled) d.setAttribute('data-theme', 'amoled'); else d.removeAttribute('data-theme');
} catch {} })();`,
          }}
        />
      </head>
      <body>
        <Providers>
          <AppHeader />
          {children}
        </Providers>
      </body>
    </html>
  );
}
