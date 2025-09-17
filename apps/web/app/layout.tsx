import './globals.css';
import React from 'react';
import Providers from './providers';
import { TenantAwareTopBar } from '../src/components/TenantAwareTopBar';

export default function RootLayout({ children }: { children: React.ReactNode }) {
  // Attempt to read the custom x-pathname header via a script (no server component header API used here in client boundary). For SSR pass, we can inline a data attribute via a small script before paint.
  return (
    <html lang="en" suppressHydrationWarning data-pathname="">
      <head>
        {/* Prevent theme flash on first paint: initialize classes before hydration */}
        <script
          dangerouslySetInnerHTML={{
            __html: `(() => { try {
  const d = document.documentElement;
  // Capture current pathname early for client components that need stable gating prior to hydration.
  d.setAttribute('data-pathname', window.location.pathname || '');
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
        <a href="#main" className="skip-link">
          Skip to main content
        </a>
        <Providers>
          <TenantAwareTopBar />
          {children}
        </Providers>
      </body>
    </html>
  );
}
