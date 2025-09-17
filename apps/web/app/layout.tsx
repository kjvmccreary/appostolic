import './globals.css';
import React from 'react';
import Providers from './providers';
import TenantSessionHydrator from '../src/components/TenantSessionHydrator';
import { cookies } from 'next/headers';
import { getServerSession } from 'next-auth';
import { authOptions } from '../src/lib/auth';
import { TopBar } from '../src/components/TopBar';

// Pure helper (exported for tests) deciding whether to show the TopBar.
// Guards against stale cookies by requiring both cookie and session alignment.
export function shouldShowTopBar(
  selectedTenantCookie: string | undefined,
  sessionTenant: string | undefined,
) {
  return !!(selectedTenantCookie && sessionTenant && selectedTenantCookie === sessionTenant);
}

export default async function RootLayout({ children }: { children: React.ReactNode }) {
  const cookieStore = cookies();
  const selectedTenantCookie = cookieStore.get('selected_tenant')?.value;
  // Fetch the server session (JWT) to validate tenant claim consistency.
  const session = await getServerSession(authOptions);
  const sessionTenant = (session as unknown as { tenant?: string } | null)?.tenant;
  // Only show TopBar when both a cookie and a matching session tenant exist to avoid stale cookie leakage.
  const showTopBar = shouldShowTopBar(selectedTenantCookie, sessionTenant);
  // Lazy-load a client hydrator that, if the cookie is present but the session tenant
  // hasn't caught up yet (JWT not re-issued), triggers a session.update to align quickly.
  return (
    <html lang="en" suppressHydrationWarning>
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
        <a href="#main" className="skip-link">
          Skip to main content
        </a>
        <Providers>
          {showTopBar ? <TopBar /> : null}
          {!showTopBar && selectedTenantCookie && !sessionTenant ? (
            <TenantSessionHydrator tenant={selectedTenantCookie} />
          ) : null}
          {children}
        </Providers>
      </body>
    </html>
  );
}
